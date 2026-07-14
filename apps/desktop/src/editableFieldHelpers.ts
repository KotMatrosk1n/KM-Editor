/* SPDX-License-Identifier: GPL-3.0-only */

export function parseEditableIntegerDraft(
  value: string,
  options?: Array<{ label: string; value: number }> | null
) {
  const normalizedValue = value.trim();
  if (normalizedValue.length === 0) {
    return null;
  }

  const optionMatch = options?.find(
    (option) =>
      option.label.toLocaleLowerCase() === normalizedValue.toLocaleLowerCase() ||
      option.value.toString() === normalizedValue
  );
  if (optionMatch) {
    return optionMatch.value;
  }

  if (!/^[+-]?\d+$/.test(normalizedValue)) {
    return null;
  }

  const parsedValue = Number(normalizedValue);
  return Number.isSafeInteger(parsedValue) ? parsedValue : null;
}
