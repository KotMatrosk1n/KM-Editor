/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useRef, type RefObject } from 'react';

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])'
].join(',');

type ModalDialogOptions = {
  canClose?: boolean;
  onClose: () => void;
};

/**
 * Gives an already-rendered modal predictable keyboard behavior without
 * coupling the modal's content or actions to a shared visual component.
 */
export function useModalDialog<TElement extends HTMLElement = HTMLElement>({
  canClose = true,
  onClose
}: ModalDialogOptions): RefObject<TElement | null> {
  const dialogRef = useRef<TElement>(null);
  const canCloseRef = useRef(canClose);
  const onCloseRef = useRef(onClose);

  useEffect(() => {
    canCloseRef.current = canClose;
    onCloseRef.current = onClose;
  }, [canClose, onClose]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) {
      return;
    }

    const previouslyFocused = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    const inertedElements = makeBackgroundInert(dialog);
    const initialFocus = getFocusableElements(dialog)[0] ?? dialog;
    initialFocus.focus({ preventScroll: true });

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && canCloseRef.current) {
        event.preventDefault();
        event.stopPropagation();
        onCloseRef.current();
        return;
      }

      if (event.key !== 'Tab') {
        return;
      }

      const focusableElements = getFocusableElements(dialog);
      if (focusableElements.length === 0) {
        event.preventDefault();
        dialog.focus({ preventScroll: true });
        return;
      }

      const first = focusableElements[0];
      const last = focusableElements[focusableElements.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown, true);
    return () => {
      document.removeEventListener('keydown', handleKeyDown, true);
      restoreBackground(inertedElements);
      if (previouslyFocused?.isConnected) {
        previouslyFocused.focus({ preventScroll: true });
      }
    };
  }, []);

  return dialogRef;
}

function getFocusableElements(dialog: HTMLElement) {
  return Array.from(dialog.querySelectorAll<HTMLElement>(focusableSelector)).filter(
    (element) => !element.hidden && element.getAttribute('aria-hidden') !== 'true'
  );
}

function makeBackgroundInert(dialog: HTMLElement) {
  const changed: Array<{ element: HTMLElement; wasInert: boolean }> = [];
  const appRoot = document.getElementById('root');
  let activeBranch: HTMLElement | null = dialog;

  while (activeBranch?.parentElement) {
    const parentElement: HTMLElement = activeBranch.parentElement;
    for (const sibling of Array.from(parentElement.children)) {
      if (!(sibling instanceof HTMLElement) || sibling === activeBranch) {
        continue;
      }
      changed.push({ element: sibling, wasInert: sibling.inert });
      sibling.inert = true;
    }

    if (parentElement === appRoot) {
      break;
    }
    activeBranch = parentElement;
  }

  return changed;
}

function restoreBackground(changed: Array<{ element: HTMLElement; wasInert: boolean }>) {
  for (const { element, wasInert } of changed) {
    if (element.isConnected) {
      element.inert = wasInert;
    }
  }
}
