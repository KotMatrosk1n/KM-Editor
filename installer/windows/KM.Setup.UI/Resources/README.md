<!-- SPDX-License-Identifier: GPL-3.0-only -->

# Installer localization

`Strings.resx` is the invariant English catalog. The `de`, `es`, `fr`, `ru`, `uk`, and `zh` satellite catalogs translate the shared phase, status, progress, and uninstall-choice copy. Every key that is not yet translated falls back to reviewed English copy.

Add translated values with the same keys as `Strings.resx`; the WPF layout and bindings do not need to change. Keep technical messages supplied by Burn verbatim so logs and Windows Installer error codes remain searchable.
