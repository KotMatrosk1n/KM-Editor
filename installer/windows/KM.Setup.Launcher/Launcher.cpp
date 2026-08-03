// SPDX-License-Identifier: GPL-3.0-only

#include <windows.h>

#include <bcrypt.h>
#include <shellapi.h>
#include <softpub.h>
#include <wincrypt.h>
#include <wintrust.h>

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cwchar>
#include <cstring>
#include <limits>
#include <new>
#include <span>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "KmInnerBundleHash.generated.h"
#include "resource.h"

namespace {

constexpr std::array<std::uint8_t, 4> kArgumentEnvelopeMagic{'K', 'M', 'A', 'R'};
constexpr std::uint32_t kArgumentEnvelopeVersion = 1;
constexpr std::size_t kMaximumArgumentCount = 256;
constexpr std::size_t kMaximumSerializedArgumentsBytes = 16U * 1024U;
constexpr std::size_t kMaximumChildCommandLineCharacters = 30U * 1000U;
constexpr DWORD kMaximumInnerBundleBytes = 512U * 1024U * 1024U;
constexpr DWORD kInvalidSignatureError = static_cast<DWORD>(TRUST_E_BAD_DIGEST);

constexpr wchar_t kBridgeVariable[] = L"KMInvocationBridged=1";
constexpr wchar_t kUpdateVariableEnabled[] = L"KMUpdateMode=1";
constexpr wchar_t kUpdateVariableDisabled[] = L"KMUpdateMode=0";
constexpr wchar_t kRelaunchVariableEnabled[] = L"KMAutoLaunch=1";
constexpr wchar_t kRelaunchVariableDisabled[] = L"KMAutoLaunch=0";
constexpr wchar_t kArgumentsVariablePrefix[] = L"KMLaunchArgumentsBase64=";
constexpr wchar_t kSuppressStartupDialogEnvironmentVariable[] = L"KM_SETUP_SUPPRESS_STARTUP_DIALOG";

class UniqueHandle final {
public:
    UniqueHandle() noexcept = default;
    explicit UniqueHandle(HANDLE handle) noexcept : handle_(handle) {}

    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;

    UniqueHandle(UniqueHandle&& other) noexcept : handle_(std::exchange(other.handle_, nullptr)) {}
    UniqueHandle& operator=(UniqueHandle&& other) noexcept {
        if (this != &other) {
            Reset();
            handle_ = std::exchange(other.handle_, nullptr);
        }
        return *this;
    }

    ~UniqueHandle() { Reset(); }

    [[nodiscard]] HANDLE Get() const noexcept { return handle_; }
    [[nodiscard]] bool IsValid() const noexcept {
        return handle_ != nullptr && handle_ != INVALID_HANDLE_VALUE;
    }

    void Reset(HANDLE replacement = nullptr) noexcept {
        if (IsValid()) {
            CloseHandle(handle_);
        }
        handle_ = replacement;
    }

private:
    HANDLE handle_ = nullptr;
};

class LocalArgv final {
public:
    LocalArgv() noexcept {
        values_ = CommandLineToArgvW(GetCommandLineW(), &count_);
    }

    LocalArgv(const LocalArgv&) = delete;
    LocalArgv& operator=(const LocalArgv&) = delete;

    ~LocalArgv() {
        if (values_ != nullptr) {
            LocalFree(values_);
        }
    }

    [[nodiscard]] bool IsValid() const noexcept { return values_ != nullptr && count_ > 0; }
    [[nodiscard]] int Count() const noexcept { return count_; }
    [[nodiscard]] wchar_t* const* Values() const noexcept { return values_; }

private:
    int count_ = 0;
    wchar_t** values_ = nullptr;
};

struct TemporaryPayload final {
    std::wstring directory;
    std::wstring executable;

    TemporaryPayload() = default;
    TemporaryPayload(const TemporaryPayload&) = delete;
    TemporaryPayload& operator=(const TemporaryPayload&) = delete;

    TemporaryPayload(TemporaryPayload&& other) noexcept
        : directory(std::exchange(other.directory, {})),
          executable(std::exchange(other.executable, {})) {}

    TemporaryPayload& operator=(TemporaryPayload&& other) noexcept {
        if (this != &other) {
            Cleanup();
            directory = std::exchange(other.directory, {});
            executable = std::exchange(other.executable, {});
        }
        return *this;
    }

    ~TemporaryPayload() { Cleanup(); }

    void Cleanup() noexcept {
        if (!executable.empty()) {
            SetFileAttributesW(executable.c_str(), FILE_ATTRIBUTE_NORMAL);
            DeleteFileW(executable.c_str());
            executable.clear();
        }
        if (!directory.empty()) {
            RemoveDirectoryW(directory.c_str());
            directory.clear();
        }
    }
};

enum class BundleAction {
    Default,
    Install,
    Modify,
    Repair,
    Uninstall,
};

enum class DisplayMode {
    Default,
    Passive,
    Quiet,
};

enum class RestartMode {
    Default,
    NoRestart,
    PromptRestart,
    ForceRestart,
};

enum class AuthenticodeState : std::uint8_t {
    Unsigned,
    Present,
};

enum class PreBootstrapFailure {
    None,
    InvalidArguments,
    PayloadPreparation,
    Launch,
    Internal,
};

struct LauncherOutcome final {
    DWORD exitCode = ERROR_SUCCESS;
    PreBootstrapFailure failure = PreBootstrapFailure::None;
};

struct ParsedInvocation final {
    BundleAction action = BundleAction::Default;
    DisplayMode display = DisplayMode::Default;
    RestartMode restart = RestartMode::Default;
    bool update = false;
    bool relaunch = false;
    bool hasApplicationArguments = false;
    std::wstring logPath;
    std::vector<std::wstring> applicationArguments;
};

[[nodiscard]] bool EqualsInsensitive(std::wstring_view left, std::wstring_view right) noexcept {
    if (left.size() != right.size()) {
        return false;
    }
    return CompareStringOrdinal(
               left.data(),
               static_cast<int>(left.size()),
               right.data(),
               static_cast<int>(right.size()),
               TRUE) == CSTR_EQUAL;
}

template <typename T>
[[nodiscard]] bool SelectExclusive(T& selected, T requested, T defaultValue) noexcept {
    if (selected != defaultValue && selected != requested) {
        return false;
    }
    selected = requested;
    return true;
}

[[nodiscard]] std::wstring_view SwitchName(std::wstring_view argument) noexcept {
    if (argument.size() < 2 || (argument.front() != L'/' && argument.front() != L'-')) {
        return {};
    }
    if (argument.size() >= 2 && argument[0] == L'-' && argument[1] == L'-') {
        return {};
    }
    return argument.substr(1);
}

[[nodiscard]] DWORD ParseInvocation(
    int argumentCount,
    wchar_t* const* arguments,
    ParsedInvocation& result) {
    for (int index = 1; index < argumentCount; ++index) {
        const std::wstring_view argument(arguments[index]);

        if (EqualsInsensitive(argument, L"/ARGS")) {
            if (result.hasApplicationArguments) {
                return ERROR_INVALID_PARAMETER;
            }
            result.hasApplicationArguments = true;
            const auto remaining = static_cast<std::size_t>(argumentCount - index - 1);
            if (remaining > kMaximumArgumentCount) {
                return ERROR_BUFFER_OVERFLOW;
            }
            result.applicationArguments.reserve(remaining);
            for (++index; index < argumentCount; ++index) {
                result.applicationArguments.emplace_back(arguments[index]);
            }
            break;
        }

        // These are Tauri's legacy NSIS-compatible switches. They are consumed,
        // not forwarded, so a slash-prefixed application argument cannot become
        // a Burn switch unless it appeared before the /ARGS trust boundary.
        if (EqualsInsensitive(argument, L"/P")) {
            if (!SelectExclusive(result.display, DisplayMode::Passive, DisplayMode::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
            continue;
        }
        if (EqualsInsensitive(argument, L"/S")) {
            if (!SelectExclusive(result.display, DisplayMode::Quiet, DisplayMode::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
            continue;
        }
        if (EqualsInsensitive(argument, L"/R")) {
            result.relaunch = true;
            continue;
        }
        if (EqualsInsensitive(argument, L"/UPDATE")) {
            result.update = true;
            continue;
        }

        const auto name = SwitchName(argument);
        if (name.empty()) {
            return ERROR_INVALID_PARAMETER;
        }

        if (EqualsInsensitive(name, L"install")) {
            if (!SelectExclusive(result.action, BundleAction::Install, BundleAction::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"modify")) {
            if (!SelectExclusive(result.action, BundleAction::Modify, BundleAction::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"repair")) {
            if (!SelectExclusive(result.action, BundleAction::Repair, BundleAction::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"uninstall")) {
            if (!SelectExclusive(result.action, BundleAction::Uninstall, BundleAction::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"passive")) {
            if (!SelectExclusive(result.display, DisplayMode::Passive, DisplayMode::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"quiet")) {
            if (!SelectExclusive(result.display, DisplayMode::Quiet, DisplayMode::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"norestart")) {
            if (!SelectExclusive(result.restart, RestartMode::NoRestart, RestartMode::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"promptrestart")) {
            if (!SelectExclusive(result.restart, RestartMode::PromptRestart, RestartMode::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"forcerestart")) {
            if (!SelectExclusive(result.restart, RestartMode::ForceRestart, RestartMode::Default)) {
                return ERROR_INVALID_PARAMETER;
            }
        } else if (EqualsInsensitive(name, L"log")) {
            if (!result.logPath.empty() || index + 1 >= argumentCount ||
                EqualsInsensitive(arguments[index + 1], L"/ARGS")) {
                return ERROR_INVALID_PARAMETER;
            }
            result.logPath = arguments[++index];
            if (result.logPath.empty() || result.logPath.size() >= 32767U) {
                return ERROR_INVALID_PARAMETER;
            }
        } else {
            // This rejects arbitrary Burn variables, internal -burn.* switches,
            // unsafe uninstall, layout, and every future switch until reviewed.
            return ERROR_INVALID_PARAMETER;
        }
    }

    const bool isInstallAction =
        result.action == BundleAction::Default || result.action == BundleAction::Install;
    if ((result.update || result.relaunch || result.hasApplicationArguments) && !isInstallAction) {
        return ERROR_INVALID_PARAMETER;
    }
    return ERROR_SUCCESS;
}

[[nodiscard]] DWORD WideToUtf8(std::wstring_view value, std::vector<std::uint8_t>& result) {
    if (value.empty()) {
        result.clear();
        return ERROR_SUCCESS;
    }
    if (value.size() > static_cast<std::size_t>(std::numeric_limits<int>::max())) {
        return ERROR_BUFFER_OVERFLOW;
    }

    const int required = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0,
        nullptr,
        nullptr);
    if (required <= 0) {
        return GetLastError();
    }

    result.resize(static_cast<std::size_t>(required));
    const int written = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        reinterpret_cast<char*>(result.data()),
        required,
        nullptr,
        nullptr);
    if (written != required) {
        const DWORD conversionError = GetLastError();
        return conversionError == ERROR_SUCCESS ? ERROR_NO_UNICODE_TRANSLATION : conversionError;
    }
    return ERROR_SUCCESS;
}

void AppendUint32LittleEndian(std::vector<std::uint8_t>& target, std::uint32_t value) {
    target.push_back(static_cast<std::uint8_t>(value & 0xFFU));
    target.push_back(static_cast<std::uint8_t>((value >> 8U) & 0xFFU));
    target.push_back(static_cast<std::uint8_t>((value >> 16U) & 0xFFU));
    target.push_back(static_cast<std::uint8_t>((value >> 24U) & 0xFFU));
}

[[nodiscard]] DWORD SerializeArguments(
    const std::vector<std::wstring>& arguments,
    std::vector<std::uint8_t>& serialized) {
    if (arguments.size() > kMaximumArgumentCount) {
        return ERROR_BUFFER_OVERFLOW;
    }

    serialized.clear();
    serialized.reserve(64U);
    serialized.insert(serialized.end(), kArgumentEnvelopeMagic.begin(), kArgumentEnvelopeMagic.end());
    AppendUint32LittleEndian(serialized, kArgumentEnvelopeVersion);
    AppendUint32LittleEndian(serialized, static_cast<std::uint32_t>(arguments.size()));

    std::vector<std::uint8_t> utf8;
    for (const auto& argument : arguments) {
        const DWORD conversionResult = WideToUtf8(argument, utf8);
        if (conversionResult != ERROR_SUCCESS) {
            return conversionResult;
        }
        if (utf8.size() > static_cast<std::size_t>(std::numeric_limits<std::uint32_t>::max()) ||
            serialized.size() + sizeof(std::uint32_t) + utf8.size() >
                kMaximumSerializedArgumentsBytes) {
            return ERROR_BUFFER_OVERFLOW;
        }
        AppendUint32LittleEndian(serialized, static_cast<std::uint32_t>(utf8.size()));
        serialized.insert(serialized.end(), utf8.begin(), utf8.end());
    }

    if (serialized.size() > kMaximumSerializedArgumentsBytes) {
        return ERROR_BUFFER_OVERFLOW;
    }
    return ERROR_SUCCESS;
}

[[nodiscard]] std::string Base64Encode(std::span<const std::uint8_t> bytes) {
    static constexpr char alphabet[] =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    std::string result;
    result.reserve(((bytes.size() + 2U) / 3U) * 4U);
    for (std::size_t index = 0; index < bytes.size(); index += 3U) {
        const std::uint32_t first = bytes[index];
        const std::uint32_t second = index + 1U < bytes.size() ? bytes[index + 1U] : 0U;
        const std::uint32_t third = index + 2U < bytes.size() ? bytes[index + 2U] : 0U;
        const std::uint32_t combined = (first << 16U) | (second << 8U) | third;

        result.push_back(alphabet[(combined >> 18U) & 0x3FU]);
        result.push_back(alphabet[(combined >> 12U) & 0x3FU]);
        result.push_back(index + 1U < bytes.size() ? alphabet[(combined >> 6U) & 0x3FU] : '=');
        result.push_back(index + 2U < bytes.size() ? alphabet[combined & 0x3FU] : '=');
    }
    return result;
}

[[nodiscard]] std::wstring QuoteWindowsArgument(std::wstring_view argument) {
    if (!argument.empty() &&
        std::none_of(argument.begin(), argument.end(), [](wchar_t character) {
            return character == L' ' || character == L'\t' || character == L'"';
        })) {
        return std::wstring(argument);
    }

    std::wstring quoted;
    quoted.reserve(argument.size() + 2U);
    quoted.push_back(L'"');
    std::size_t backslashes = 0;
    for (const wchar_t character : argument) {
        if (character == L'\\') {
            ++backslashes;
            continue;
        }
        if (character == L'"') {
            quoted.append((backslashes * 2U) + 1U, L'\\');
            quoted.push_back(L'"');
            backslashes = 0;
            continue;
        }
        quoted.append(backslashes, L'\\');
        backslashes = 0;
        quoted.push_back(character);
    }
    quoted.append(backslashes * 2U, L'\\');
    quoted.push_back(L'"');
    return quoted;
}

[[nodiscard]] DWORD BuildBurnArguments(
    const ParsedInvocation& invocation,
    std::vector<std::wstring>& burnArguments) {
    burnArguments.clear();

    switch (invocation.action) {
        case BundleAction::Default:
            break;
        case BundleAction::Install:
            burnArguments.emplace_back(L"-install");
            break;
        case BundleAction::Modify:
            burnArguments.emplace_back(L"-modify");
            break;
        case BundleAction::Repair:
            burnArguments.emplace_back(L"-repair");
            break;
        case BundleAction::Uninstall:
            burnArguments.emplace_back(L"-uninstall");
            break;
    }

    switch (invocation.display) {
        case DisplayMode::Default:
            break;
        case DisplayMode::Passive:
            burnArguments.emplace_back(L"-passive");
            break;
        case DisplayMode::Quiet:
            burnArguments.emplace_back(L"-quiet");
            break;
    }

    switch (invocation.restart) {
        case RestartMode::Default:
            // Tauri's /R means relaunch the application, not restart Windows.
            // Never let a passive or quiet in-app update reboot the machine.
            if (invocation.update) {
                burnArguments.emplace_back(L"-norestart");
            }
            break;
        case RestartMode::NoRestart:
            burnArguments.emplace_back(L"-norestart");
            break;
        case RestartMode::PromptRestart:
            burnArguments.emplace_back(L"-promptrestart");
            break;
        case RestartMode::ForceRestart:
            burnArguments.emplace_back(L"-forcerestart");
            break;
    }

    if (!invocation.logPath.empty()) {
        burnArguments.emplace_back(L"-log");
        burnArguments.push_back(invocation.logPath);
    }

    // Always emit a valid versioned envelope, including for zero application
    // arguments, so a bridged invocation never relies on an empty-value special case.
    std::vector<std::uint8_t> serialized;
    const DWORD serializationResult =
        SerializeArguments(invocation.applicationArguments, serialized);
    if (serializationResult != ERROR_SUCCESS) {
        return serializationResult;
    }

    const std::string encoded = Base64Encode(serialized);
    const std::wstring encodedWide(encoded.begin(), encoded.end());

    burnArguments.emplace_back(kBridgeVariable);
    burnArguments.emplace_back(invocation.update ? kUpdateVariableEnabled : kUpdateVariableDisabled);
    burnArguments.emplace_back(
        invocation.relaunch ? kRelaunchVariableEnabled : kRelaunchVariableDisabled);
    burnArguments.emplace_back(std::wstring(kArgumentsVariablePrefix) + encodedWide);

    if (!serialized.empty()) {
        SecureZeroMemory(serialized.data(), serialized.size());
    }
    return ERROR_SUCCESS;
}

[[nodiscard]] DWORD BuildChildCommandLine(
    const std::wstring& executable,
    const std::vector<std::wstring>& arguments,
    std::wstring& commandLine) {
    commandLine = QuoteWindowsArgument(executable);
    for (const auto& argument : arguments) {
        commandLine.push_back(L' ');
        commandLine.append(QuoteWindowsArgument(argument));
        if (commandLine.size() > kMaximumChildCommandLineCharacters) {
            return ERROR_FILENAME_EXCED_RANGE;
        }
    }
    return ERROR_SUCCESS;
}

class Sha256 final {
public:
    Sha256() = default;
    Sha256(const Sha256&) = delete;
    Sha256& operator=(const Sha256&) = delete;

    ~Sha256() {
        if (hash_ != nullptr) {
            BCryptDestroyHash(hash_);
        }
        if (algorithm_ != nullptr) {
            BCryptCloseAlgorithmProvider(algorithm_, 0);
        }
    }

    [[nodiscard]] DWORD Initialize() {
        if (BCryptOpenAlgorithmProvider(&algorithm_, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) {
            return ERROR_INVALID_FUNCTION;
        }

        DWORD objectLength = 0;
        DWORD copied = 0;
        if (BCryptGetProperty(
                algorithm_,
                BCRYPT_OBJECT_LENGTH,
                reinterpret_cast<PUCHAR>(&objectLength),
                sizeof(objectLength),
                &copied,
                0) < 0 ||
            copied != sizeof(objectLength)) {
            return ERROR_INVALID_DATA;
        }
        object_.resize(objectLength);
        if (BCryptCreateHash(
                algorithm_,
                &hash_,
                object_.data(),
                static_cast<ULONG>(object_.size()),
                nullptr,
                0,
                0) < 0) {
            return ERROR_INVALID_DATA;
        }
        return ERROR_SUCCESS;
    }

    [[nodiscard]] DWORD Append(std::span<const std::uint8_t> bytes) {
        std::size_t offset = 0;
        while (offset < bytes.size()) {
            const auto remaining = bytes.size() - offset;
            const ULONG chunk = static_cast<ULONG>(std::min<std::size_t>(
                remaining,
                static_cast<std::size_t>(std::numeric_limits<ULONG>::max())));
            if (BCryptHashData(
                    hash_,
                    const_cast<PUCHAR>(bytes.data() + offset),
                    chunk,
                    0) < 0) {
                return ERROR_INVALID_DATA;
            }
            offset += chunk;
        }
        return ERROR_SUCCESS;
    }

    [[nodiscard]] DWORD Finish(std::array<std::uint8_t, 32>& digest) {
        return BCryptFinishHash(hash_, digest.data(), static_cast<ULONG>(digest.size()), 0) < 0
                   ? ERROR_INVALID_DATA
                   : ERROR_SUCCESS;
    }

private:
    BCRYPT_ALG_HANDLE algorithm_ = nullptr;
    BCRYPT_HASH_HANDLE hash_ = nullptr;
    std::vector<std::uint8_t> object_;
};

[[nodiscard]] DWORD ComputeSha256(
    std::span<const std::uint8_t> bytes,
    std::array<std::uint8_t, 32>& digest) {
    Sha256 sha256;
    DWORD result = sha256.Initialize();
    if (result == ERROR_SUCCESS) {
        result = sha256.Append(bytes);
    }
    if (result == ERROR_SUCCESS) {
        result = sha256.Finish(digest);
    }
    return result;
}

[[nodiscard]] int HexValue(wchar_t character) noexcept {
    if (character >= L'0' && character <= L'9') {
        return character - L'0';
    }
    if (character >= L'a' && character <= L'f') {
        return 10 + (character - L'a');
    }
    if (character >= L'A' && character <= L'F') {
        return 10 + (character - L'A');
    }
    return -1;
}

[[nodiscard]] bool ParseExpectedDigest(std::array<std::uint8_t, 32>& digest) noexcept {
    constexpr std::wstring_view encoded(KM_INNER_BUNDLE_SHA256_HEX);
    if (encoded.size() != digest.size() * 2U) {
        return false;
    }
    for (std::size_t index = 0; index < digest.size(); ++index) {
        const int high = HexValue(encoded[index * 2U]);
        const int low = HexValue(encoded[(index * 2U) + 1U]);
        if (high < 0 || low < 0) {
            return false;
        }
        digest[index] = static_cast<std::uint8_t>((high << 4) | low);
    }
    return true;
}

[[nodiscard]] bool ConstantTimeEqual(
    const std::array<std::uint8_t, 32>& left,
    const std::array<std::uint8_t, 32>& right) noexcept {
    std::uint8_t difference = 0;
    for (std::size_t index = 0; index < left.size(); ++index) {
        difference = static_cast<std::uint8_t>(difference | (left[index] ^ right[index]));
    }
    return difference == 0;
}

[[nodiscard]] DWORD ValidateX64Pe(
    std::span<const std::uint8_t> bytes,
    AuthenticodeState& authenticode) noexcept {
    authenticode = AuthenticodeState::Unsigned;
    if (bytes.size() < sizeof(IMAGE_DOS_HEADER)) {
        return ERROR_BAD_EXE_FORMAT;
    }

    IMAGE_DOS_HEADER dosHeader{};
    std::memcpy(&dosHeader, bytes.data(), sizeof(dosHeader));
    if (dosHeader.e_magic != IMAGE_DOS_SIGNATURE || dosHeader.e_lfanew < 0) {
        return ERROR_BAD_EXE_FORMAT;
    }

    const auto ntOffset = static_cast<std::size_t>(dosHeader.e_lfanew);
    if (ntOffset > bytes.size() || bytes.size() - ntOffset < sizeof(IMAGE_NT_HEADERS64)) {
        return ERROR_BAD_EXE_FORMAT;
    }

    IMAGE_NT_HEADERS64 ntHeaders{};
    std::memcpy(&ntHeaders, bytes.data() + ntOffset, sizeof(ntHeaders));
    if (ntHeaders.Signature != IMAGE_NT_SIGNATURE ||
        ntHeaders.FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
        ntHeaders.FileHeader.SizeOfOptionalHeader < sizeof(IMAGE_OPTIONAL_HEADER64) ||
        ntHeaders.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC) {
        return ERROR_BAD_EXE_FORMAT;
    }

    if (ntHeaders.OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_SECURITY) {
        return ERROR_SUCCESS;
    }

    const auto security = ntHeaders.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_SECURITY];
    const auto certificateOffset = static_cast<std::size_t>(security.VirtualAddress);
    const auto certificateSize = static_cast<std::size_t>(security.Size);
    if (certificateOffset == 0 && certificateSize == 0) {
        return ERROR_SUCCESS;
    }
    if (certificateOffset == 0 || certificateSize < sizeof(WIN_CERTIFICATE) ||
        certificateOffset > bytes.size() || certificateSize > bytes.size() - certificateOffset) {
        return kInvalidSignatureError;
    }

    WIN_CERTIFICATE certificate{};
    std::memcpy(&certificate, bytes.data() + certificateOffset, sizeof(certificate));
    if (certificate.dwLength < sizeof(WIN_CERTIFICATE) || certificate.dwLength > certificateSize ||
        certificate.wRevision != WIN_CERT_REVISION_2_0 ||
        certificate.wCertificateType != WIN_CERT_TYPE_PKCS_SIGNED_DATA) {
        return kInvalidSignatureError;
    }

    authenticode = AuthenticodeState::Present;
    return ERROR_SUCCESS;
}

[[nodiscard]] DWORD LoadPinnedResource(
    std::span<const std::uint8_t>& resourceBytes,
    std::array<std::uint8_t, 32>& expectedDigest,
    AuthenticodeState& authenticode) {
    if (!ParseExpectedDigest(expectedDigest)) {
        return ERROR_INVALID_DATA;
    }

    const HRSRC resource =
        FindResourceW(nullptr, MAKEINTRESOURCEW(IDR_KM_INNER_BUNDLE), RT_RCDATA);
    if (resource == nullptr) {
        return GetLastError();
    }
    const DWORD size = SizeofResource(nullptr, resource);
    if (size == 0 || size > kMaximumInnerBundleBytes) {
        return ERROR_FILE_TOO_LARGE;
    }
    const HGLOBAL loaded = LoadResource(nullptr, resource);
    if (loaded == nullptr) {
        return GetLastError();
    }
    const auto* data = static_cast<const std::uint8_t*>(LockResource(loaded));
    if (data == nullptr) {
        return ERROR_RESOURCE_DATA_NOT_FOUND;
    }
    resourceBytes = std::span<const std::uint8_t>(data, size);

    DWORD result = ValidateX64Pe(resourceBytes, authenticode);
    if (result != ERROR_SUCCESS) {
        return result;
    }

    std::array<std::uint8_t, 32> actualDigest{};
    result = ComputeSha256(resourceBytes, actualDigest);
    if (result == ERROR_SUCCESS && !ConstantTimeEqual(actualDigest, expectedDigest)) {
        result = ERROR_CRC;
    }
    SecureZeroMemory(actualDigest.data(), actualDigest.size());
    return result;
}

[[nodiscard]] DWORD RandomHex(std::wstring& output) {
    std::array<std::uint8_t, 16> random{};
    if (BCryptGenRandom(
            nullptr,
            random.data(),
            static_cast<ULONG>(random.size()),
            BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) {
        return ERROR_GEN_FAILURE;
    }

    static constexpr wchar_t hex[] = L"0123456789abcdef";
    output.clear();
    output.reserve(random.size() * 2U);
    for (const auto value : random) {
        output.push_back(hex[value >> 4U]);
        output.push_back(hex[value & 0x0FU]);
    }
    SecureZeroMemory(random.data(), random.size());
    return ERROR_SUCCESS;
}

[[nodiscard]] DWORD GetUserTempDirectory(std::wstring& directory) {
    std::vector<wchar_t> buffer(32768U, L'\0');
    const DWORD length = GetTempPathW(static_cast<DWORD>(buffer.size()), buffer.data());
    if (length == 0) {
        return GetLastError();
    }
    if (length >= buffer.size()) {
        return ERROR_INSUFFICIENT_BUFFER;
    }
    directory.assign(buffer.data(), length);
    if (directory.empty() || (directory.back() != L'\\' && directory.back() != L'/')) {
        directory.push_back(L'\\');
    }
    return ERROR_SUCCESS;
}

[[nodiscard]] DWORD CreateUniqueTemporaryPayload(TemporaryPayload& payload) {
    std::wstring tempRoot;
    DWORD result = GetUserTempDirectory(tempRoot);
    if (result != ERROR_SUCCESS) {
        return result;
    }

    for (unsigned int attempt = 0; attempt < 64U; ++attempt) {
        std::wstring random;
        result = RandomHex(random);
        if (result != ERROR_SUCCESS) {
            return result;
        }

        std::wstring candidate = tempRoot + L"KM.Setup." + std::to_wstring(GetCurrentProcessId()) +
                                 L"." + random;
        if (!CreateDirectoryW(candidate.c_str(), nullptr)) {
            const DWORD creationError = GetLastError();
            if (creationError == ERROR_ALREADY_EXISTS || creationError == ERROR_FILE_EXISTS) {
                continue;
            }
            return creationError;
        }

        const DWORD attributes = GetFileAttributesW(candidate.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES ||
            (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
            (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0) {
            RemoveDirectoryW(candidate.c_str());
            return ERROR_CANT_ACCESS_FILE;
        }

        payload.directory = std::move(candidate);
        payload.executable = payload.directory + L"\\KM Editor Setup.inner.exe";
        return ERROR_SUCCESS;
    }
    return ERROR_ALREADY_EXISTS;
}

[[nodiscard]] DWORD WriteAll(HANDLE file, std::span<const std::uint8_t> bytes) {
    std::size_t offset = 0;
    while (offset < bytes.size()) {
        const DWORD chunk = static_cast<DWORD>(std::min<std::size_t>(
            bytes.size() - offset,
            static_cast<std::size_t>(std::numeric_limits<DWORD>::max())));
        DWORD written = 0;
        if (!WriteFile(file, bytes.data() + offset, chunk, &written, nullptr)) {
            return GetLastError();
        }
        if (written == 0) {
            return ERROR_WRITE_FAULT;
        }
        offset += written;
    }
    return ERROR_SUCCESS;
}

[[nodiscard]] DWORD HashFile(HANDLE file, std::array<std::uint8_t, 32>& digest) {
    LARGE_INTEGER beginning{};
    if (!SetFilePointerEx(file, beginning, nullptr, FILE_BEGIN)) {
        return GetLastError();
    }

    Sha256 sha256;
    DWORD result = sha256.Initialize();
    std::array<std::uint8_t, 64U * 1024U> buffer{};
    while (result == ERROR_SUCCESS) {
        DWORD read = 0;
        if (!ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr)) {
            result = GetLastError();
            break;
        }
        if (read == 0) {
            break;
        }
        result = sha256.Append(std::span<const std::uint8_t>(buffer.data(), read));
    }
    if (result == ERROR_SUCCESS) {
        result = sha256.Finish(digest);
    }
    SecureZeroMemory(buffer.data(), buffer.size());
    return result;
}

[[nodiscard]] DWORD VerifyAuthenticode(const std::wstring& executable) {
    WINTRUST_FILE_INFO fileInfo{};
    fileInfo.cbStruct = sizeof(fileInfo);
    fileInfo.pcwszFilePath = executable.c_str();

    WINTRUST_DATA trustData{};
    trustData.cbStruct = sizeof(trustData);
    trustData.dwUIChoice = WTD_UI_NONE;
    trustData.fdwRevocationChecks = WTD_REVOKE_NONE;
    trustData.dwUnionChoice = WTD_CHOICE_FILE;
    trustData.pFile = &fileInfo;
    trustData.dwStateAction = WTD_STATEACTION_VERIFY;
    trustData.dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_SAFER_FLAG;

    GUID policy = WINTRUST_ACTION_GENERIC_VERIFY_V2;
    const LONG status = WinVerifyTrust(nullptr, &policy, &trustData);
    trustData.dwStateAction = WTD_STATEACTION_CLOSE;
    WinVerifyTrust(nullptr, &policy, &trustData);
    return status == ERROR_SUCCESS ? ERROR_SUCCESS : kInvalidSignatureError;
}

[[nodiscard]] DWORD ExtractAndVerifyPayload(
    std::span<const std::uint8_t> resourceBytes,
    const std::array<std::uint8_t, 32>& expectedDigest,
    AuthenticodeState authenticode,
    TemporaryPayload& payload,
    UniqueHandle& pinnedReadHandle) {
    DWORD result = CreateUniqueTemporaryPayload(payload);
    if (result != ERROR_SUCCESS) {
        return result;
    }

    UniqueHandle writeHandle(CreateFileW(
        payload.executable.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ,
        nullptr,
        CREATE_NEW,
        FILE_ATTRIBUTE_TEMPORARY | FILE_ATTRIBUTE_NOT_CONTENT_INDEXED | FILE_FLAG_WRITE_THROUGH,
        nullptr));
    if (!writeHandle.IsValid()) {
        return GetLastError();
    }

    result = WriteAll(writeHandle.Get(), resourceBytes);
    if (result == ERROR_SUCCESS && !FlushFileBuffers(writeHandle.Get())) {
        result = GetLastError();
    }
    writeHandle.Reset();
    if (result != ERROR_SUCCESS) {
        return result;
    }

    pinnedReadHandle.Reset(CreateFileW(
        payload.executable.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr));
    if (!pinnedReadHandle.IsValid()) {
        return GetLastError();
    }

    BY_HANDLE_FILE_INFORMATION fileInformation{};
    if (!GetFileInformationByHandle(pinnedReadHandle.Get(), &fileInformation)) {
        return GetLastError();
    }
    if ((fileInformation.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        fileInformation.nFileSizeHigh != 0 || fileInformation.nFileSizeLow != resourceBytes.size()) {
        return ERROR_FILE_INVALID;
    }

    std::array<std::uint8_t, 32> extractedDigest{};
    result = HashFile(pinnedReadHandle.Get(), extractedDigest);
    if (result == ERROR_SUCCESS && !ConstantTimeEqual(extractedDigest, expectedDigest)) {
        result = ERROR_CRC;
    }
    SecureZeroMemory(extractedDigest.data(), extractedDigest.size());
    if (result != ERROR_SUCCESS) {
        return result;
    }
    return authenticode == AuthenticodeState::Unsigned
        ? ERROR_SUCCESS
        : VerifyAuthenticode(payload.executable);
}

[[nodiscard]] DWORD LaunchAndWait(
    const TemporaryPayload& payload,
    const std::vector<std::wstring>& burnArguments,
    bool suppressStartupDialog,
    DWORD& childExitCode) {
    std::wstring commandLine;
    DWORD result = BuildChildCommandLine(payload.executable, burnArguments, commandLine);
    if (result != ERROR_SUCCESS) {
        return result;
    }

    std::vector<wchar_t> mutableCommandLine(commandLine.begin(), commandLine.end());
    mutableCommandLine.push_back(L'\0');

    STARTUPINFOW startupInfo{};
    startupInfo.cb = sizeof(startupInfo);
    PROCESS_INFORMATION processInformation{};
    if (!SetEnvironmentVariableW(
            kSuppressStartupDialogEnvironmentVariable,
            suppressStartupDialog ? L"1" : L"0")) {
        return GetLastError();
    }

    if (!CreateProcessW(
            payload.executable.c_str(),
            mutableCommandLine.data(),
            nullptr,
            nullptr,
            FALSE,
            CREATE_UNICODE_ENVIRONMENT,
            nullptr,
            payload.directory.c_str(),
            &startupInfo,
            &processInformation)) {
        return GetLastError();
    }

    UniqueHandle process(processInformation.hProcess);
    UniqueHandle thread(processInformation.hThread);
    SecureZeroMemory(mutableCommandLine.data(), mutableCommandLine.size() * sizeof(wchar_t));
    SecureZeroMemory(commandLine.data(), commandLine.size() * sizeof(wchar_t));

    const DWORD waitResult = WaitForSingleObject(process.Get(), INFINITE);
    if (waitResult != WAIT_OBJECT_0) {
        return waitResult == WAIT_FAILED ? GetLastError() : ERROR_GEN_FAILURE;
    }
    if (!GetExitCodeProcess(process.Get(), &childExitCode)) {
        return GetLastError();
    }
    return ERROR_SUCCESS;
}

[[nodiscard]] LauncherOutcome RunLauncher() {
    LocalArgv commandLine;
    if (!commandLine.IsValid()) {
        const DWORD parsingError = GetLastError();
        return {
            parsingError == ERROR_SUCCESS ? ERROR_INVALID_PARAMETER : parsingError,
            PreBootstrapFailure::InvalidArguments};
    }

    ParsedInvocation invocation;
    DWORD result = ParseInvocation(commandLine.Count(), commandLine.Values(), invocation);
    if (result != ERROR_SUCCESS) {
        return {result, PreBootstrapFailure::InvalidArguments};
    }

    std::vector<std::wstring> burnArguments;
    result = BuildBurnArguments(invocation, burnArguments);
    if (result != ERROR_SUCCESS) {
        return {result, PreBootstrapFailure::InvalidArguments};
    }

    std::span<const std::uint8_t> resourceBytes;
    std::array<std::uint8_t, 32> expectedDigest{};
    AuthenticodeState authenticode = AuthenticodeState::Unsigned;
    result = LoadPinnedResource(resourceBytes, expectedDigest, authenticode);
    if (result != ERROR_SUCCESS) {
        return {result, PreBootstrapFailure::PayloadPreparation};
    }

    TemporaryPayload payload;
    UniqueHandle pinnedReadHandle;
    result = ExtractAndVerifyPayload(
        resourceBytes,
        expectedDigest,
        authenticode,
        payload,
        pinnedReadHandle);
    SecureZeroMemory(expectedDigest.data(), expectedDigest.size());
    if (result != ERROR_SUCCESS) {
        return {result, PreBootstrapFailure::PayloadPreparation};
    }

    DWORD childExitCode = ERROR_GEN_FAILURE;
    result = LaunchAndWait(
        payload,
        burnArguments,
        invocation.display == DisplayMode::Quiet,
        childExitCode);
    pinnedReadHandle.Reset();
    if (result != ERROR_SUCCESS) {
        return {result, PreBootstrapFailure::Launch};
    }
    // Burn owns all user-visible failures after CreateProcess succeeds. A child
    // cancellation or nonzero installer exit must not trigger a second dialog.
    return {childExitCode, PreBootstrapFailure::None};
}

void ShowPreBootstrapFailure(PreBootstrapFailure failure, DWORD exitCode) noexcept {
    const wchar_t* explanation = nullptr;
    switch (failure) {
        case PreBootstrapFailure::InvalidArguments:
            explanation =
                L"KM Editor Setup could not understand its update arguments.\n\n"
                L"Please download the latest KM Editor installer and try again.";
            break;
        case PreBootstrapFailure::PayloadPreparation:
            explanation =
                L"KM Editor Setup could not verify or prepare its embedded installer.\n\n"
                L"The download may be damaged. Please download a fresh copy and try again.";
            break;
        case PreBootstrapFailure::Launch:
            explanation =
                L"KM Editor Setup could not start its installer engine.\n\n"
                L"Close other installers, then try again with a fresh download.";
            break;
        case PreBootstrapFailure::Internal:
            explanation =
                L"KM Editor Setup could not start because of an internal error.\n\n"
                L"Restart Windows, then try again with a fresh download.";
            break;
        case PreBootstrapFailure::None:
            return;
    }

    std::array<wchar_t, 512> message{};
    const int written = swprintf_s(
        message.data(),
        message.size(),
        L"%ls\n\nError code: 0x%08lX",
        explanation,
        exitCode);
    if (written < 0) {
        MessageBoxW(
            nullptr,
            explanation,
            L"KM Editor Setup",
            MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TASKMODAL);
        return;
    }

    MessageBoxW(
        nullptr,
        message.data(),
        L"KM Editor Setup",
        MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TASKMODAL);
}

[[nodiscard]] bool CommandLineRequestsQuietDisplay() noexcept {
    LocalArgv commandLine;
    if (!commandLine.IsValid()) {
        return false;
    }

    for (int index = 1; index < commandLine.Count(); ++index) {
        const std::wstring_view argument(commandLine.Values()[index]);
        if (EqualsInsensitive(argument, L"/ARGS")) {
            break;
        }

        if (EqualsInsensitive(argument, L"/S")) {
            return true;
        }

        const auto name = SwitchName(argument);
        if (EqualsInsensitive(name, L"log")) {
            ++index;
            continue;
        }
        if (EqualsInsensitive(name, L"quiet")) {
            return true;
        }
    }

    return false;
}

}  // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    const bool suppressFailureUi = CommandLineRequestsQuietDisplay();
    LauncherOutcome outcome;
    try {
        outcome = RunLauncher();
    } catch (const std::bad_alloc&) {
        outcome = {ERROR_NOT_ENOUGH_MEMORY, PreBootstrapFailure::Internal};
    } catch (...) {
        outcome = {ERROR_UNHANDLED_EXCEPTION, PreBootstrapFailure::Internal};
    }

    if (outcome.failure != PreBootstrapFailure::None && !suppressFailureUi) {
        ShowPreBootstrapFailure(outcome.failure, outcome.exitCode);
    }
    return static_cast<int>(outcome.exitCode);
}
