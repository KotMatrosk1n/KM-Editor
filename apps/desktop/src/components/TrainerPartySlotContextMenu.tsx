/* SPDX-License-Identifier: GPL-3.0-only */

import { ClipboardPaste, Copy } from 'lucide-react';
import {
  type CSSProperties,
  type KeyboardEvent,
  useEffect,
  useLayoutEffect,
  useRef,
  useState
} from 'react';
import { createPortal } from 'react-dom';
import { useLocalization } from '../localization/LocalizationProvider';
import { getEditorPortalHost } from './editorPortal';

type MenuPosition = {
  left: number;
  top: number;
};

export type TrainerPartySlotContextMenuProps = {
  copyActionLabel?: string;
  copyDisabledReason?: string;
  id?: string;
  left: number;
  menuLabel?: string;
  noSourceSummaryLabel?: string;
  onClose: () => void;
  onCopy: () => void;
  onPaste: () => void;
  pasteActionLabel?: string;
  pasteDisabledReason?: string;
  sourceLabel?: string;
  targetLabel: string;
  top: number;
  triggerElement: HTMLElement | null;
};

const contextMenuEdgeGap = 10;
export const trainerPartySlotContextMenuId = 'trainer-party-slot-context-menu';

export function TrainerPartySlotContextMenu({
  copyActionLabel,
  copyDisabledReason,
  id = trainerPartySlotContextMenuId,
  left,
  menuLabel,
  noSourceSummaryLabel,
  onClose,
  onCopy,
  onPaste,
  pasteActionLabel,
  pasteDisabledReason,
  sourceLabel,
  targetLabel,
  top,
  triggerElement
}: TrainerPartySlotContextMenuProps) {
  const { t } = useLocalization();
  const menuRef = useRef<HTMLDivElement | null>(null);
  const [position, setPosition] = useState<MenuPosition>({ left, top });
  const [isPositioned, setIsPositioned] = useState(false);

  const closeAndRestoreFocus = () => {
    onClose();
    window.requestAnimationFrame(() => triggerElement?.focus());
  };

  useLayoutEffect(() => {
    const menu = menuRef.current;
    if (!menu) {
      return;
    }

    const bounds = menu.getBoundingClientRect();
    const maximumLeft = Math.max(
      contextMenuEdgeGap,
      window.innerWidth - contextMenuEdgeGap - bounds.width
    );
    const maximumTop = Math.max(
      contextMenuEdgeGap,
      window.innerHeight - contextMenuEdgeGap - bounds.height
    );
    setPosition({
      left: Math.min(Math.max(contextMenuEdgeGap, left), maximumLeft),
      top: Math.min(Math.max(contextMenuEdgeGap, top), maximumTop)
    });
    setIsPositioned(true);

    const firstAction = menu.querySelector<HTMLButtonElement>('[role="menuitem"]');
    (firstAction ?? menu).focus();
  }, [left, top]);

  useEffect(() => {
    const handlePointerDown = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) {
        onClose();
      }
    };
    const handleDismiss = () => onClose();

    document.addEventListener('pointerdown', handlePointerDown);
    window.addEventListener('blur', handleDismiss);
    window.addEventListener('resize', handleDismiss);
    window.addEventListener('scroll', handleDismiss, true);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      window.removeEventListener('blur', handleDismiss);
      window.removeEventListener('resize', handleDismiss);
      window.removeEventListener('scroll', handleDismiss, true);
    };
  }, [onClose]);

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      closeAndRestoreFocus();
      return;
    }

    if (event.key === 'Tab') {
      event.preventDefault();
      event.stopPropagation();
      onClose();
      window.requestAnimationFrame(() =>
        focusAdjacentToTrigger(triggerElement, event.shiftKey ? -1 : 1)
      );
      return;
    }

    if (
      event.key !== 'ArrowDown' &&
      event.key !== 'ArrowUp' &&
      event.key !== 'Home' &&
      event.key !== 'End'
    ) {
      return;
    }

    const menuItems = Array.from(
      menuRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? []
    );
    if (menuItems.length === 0) {
      return;
    }

    event.preventDefault();
    const currentIndex = menuItems.findIndex((action) => action === document.activeElement);
    const nextIndex =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? menuItems.length - 1
          : event.key === 'ArrowDown'
            ? currentIndex < 0
              ? 0
              : (currentIndex + 1) % menuItems.length
            : currentIndex < 0
              ? menuItems.length - 1
              : (currentIndex - 1 + menuItems.length) % menuItems.length;
    menuItems[nextIndex]?.focus();
  };

  const portalHost = getEditorPortalHost();
  if (!portalHost) {
    return null;
  }

  return createPortal(
    <div
      aria-label={menuLabel ?? t('trainers.partyClipboard.menuLabel', { target: targetLabel })}
      className="trainer-party-context-menu"
      data-positioned={isPositioned}
      id={id}
      onContextMenu={(event) => event.preventDefault()}
      onKeyDown={handleKeyDown}
      ref={menuRef}
      role="menu"
      style={
        {
          '--trainer-context-menu-left': `${position.left}px`,
          '--trainer-context-menu-top': `${position.top}px`
        } as CSSProperties
      }
      tabIndex={-1}
    >
      <div className="trainer-party-context-menu-heading" role="presentation">
        <strong>{targetLabel}</strong>
        <small>
          {sourceLabel ?? noSourceSummaryLabel ?? t('trainers.partyClipboard.noSourceSummary')}
        </small>
      </div>
      <button
        aria-disabled={copyDisabledReason !== undefined}
        onClick={() => {
          if (copyDisabledReason !== undefined) {
            return;
          }

          onCopy();
          closeAndRestoreFocus();
        }}
        role="menuitem"
        type="button"
      >
        <Copy aria-hidden="true" size={16} />
        <span>
          <strong>{copyActionLabel ?? t('trainers.partyClipboard.copyAction')}</strong>
          {copyDisabledReason ? <small>{copyDisabledReason}</small> : null}
        </span>
      </button>
      <button
        aria-disabled={pasteDisabledReason !== undefined}
        onClick={() => {
          if (pasteDisabledReason !== undefined) {
            return;
          }

          onPaste();
          closeAndRestoreFocus();
        }}
        role="menuitem"
        type="button"
      >
        <ClipboardPaste aria-hidden="true" size={16} />
        <span>
          <strong>{pasteActionLabel ?? t('trainers.partyClipboard.pasteAction')}</strong>
          {pasteDisabledReason ? <small>{pasteDisabledReason}</small> : null}
        </span>
      </button>
    </div>,
    portalHost
  );
}

function focusAdjacentToTrigger(
  triggerElement: HTMLElement | null,
  direction: -1 | 1
) {
  if (!triggerElement?.isConnected) {
    return;
  }

  const focusableElements = Array.from(
    document.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
    )
  ).filter(
    (element) =>
      element.getAttribute('aria-hidden') !== 'true' &&
      (element.offsetWidth > 0 || element.offsetHeight > 0 || element.getClientRects().length > 0)
  );
  const triggerIndex = focusableElements.indexOf(triggerElement);
  const destination = triggerIndex < 0
    ? null
    : focusableElements[triggerIndex + direction] ?? null;
  (destination ?? triggerElement).focus();
}
