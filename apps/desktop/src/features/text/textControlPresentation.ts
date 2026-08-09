const lineBreakMarker = ' ↵ ';
const tabMarker = ' ⇥ ';

export function canonicalTextToEditorText(value: string) {
  let result = '';

  for (let index = 0; index < value.length; index += 1) {
    const current = value[index];
    if (current !== '\\' || index + 1 >= value.length) {
      result += current;
      continue;
    }

    const escaped = value[index + 1];
    if (escaped === '\\') {
      result += '\\\\';
      index += 1;
      continue;
    }

    if (escaped === 'n') {
      result += '\n';
      index += 1;
      continue;
    }

    result += `\\${escaped}`;
    index += 1;
  }

  return result;
}

export function editorTextToCanonicalText(value: string) {
  return value.replace(/\r\n|\r|\n/g, '\\n').replace(/\t/g, '\\t');
}

export function normalizeEditorTextInput(
  value: string,
  selectionStart: number,
  selectionEnd: number
) {
  const canonicalValue = editorTextToCanonicalText(value);
  const normalizePosition = (position: number) => {
    const boundedPosition = Math.max(0, Math.min(position, value.length));
    return canonicalTextToEditorText(
      editorTextToCanonicalText(value.slice(0, boundedPosition))
    ).length;
  };

  return {
    canonicalValue,
    selectionEnd: normalizePosition(selectionEnd),
    selectionStart: normalizePosition(selectionStart)
  };
}

export function insertCanonicalTextControl(
  displayValue: string,
  canonicalToken: string,
  selectionStart: number,
  selectionEnd: number
) {
  const start = Math.max(0, Math.min(selectionStart, displayValue.length));
  const end = Math.max(start, Math.min(selectionEnd, displayValue.length));
  const displayToken = canonicalTextToEditorText(canonicalToken);
  const nextDisplayValue = `${displayValue.slice(0, start)}${displayToken}${displayValue.slice(end)}`;

  return {
    canonicalValue: editorTextToCanonicalText(nextDisplayValue),
    cursorPosition: start + displayToken.length
  };
}

export function formatCanonicalTextSummary(value: string) {
  let result = '';

  for (let index = 0; index < value.length; index += 1) {
    const current = value[index];
    if (current === '\n') {
      result += lineBreakMarker;
      continue;
    }

    if (current === '\t') {
      result += tabMarker;
      continue;
    }

    if (current !== '\\' || index + 1 >= value.length) {
      result += current;
      continue;
    }

    const escaped = value[index + 1];
    switch (escaped) {
      case 'n':
        result += lineBreakMarker;
        index += 1;
        break;
      case 't':
        result += tabMarker;
        index += 1;
        break;
      case '\\':
      case '[':
      case '{':
      case '|':
      case '}':
        result += `\\${escaped}`;
        index += 1;
        break;
      default:
        result += current;
        break;
    }
  }

  return result;
}
