/* SPDX-License-Identifier: GPL-3.0-only */

import { MoreHorizontal } from 'lucide-react';
import { useEffect, useId, useRef } from 'react';
import { useLocalization } from '../../localization';

export type WorkspaceOverflowAction = {
  disabledReasonKey: string | null;
  id: string;
  isEnabled: boolean;
  labelKey: string;
};

export type WorkspaceOverflowMenuProps = {
  actions: readonly WorkspaceOverflowAction[];
  isOpen: boolean;
  onClose: () => void;
  onExecute: (actionId: string) => void;
  onToggle: () => void;
};

export function WorkspaceOverflowMenu({
  actions,
  isOpen,
  onClose,
  onExecute,
  onToggle
}: WorkspaceOverflowMenuProps) {
  const { t } = useLocalization();
  const menuId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!isOpen) {
      return;
    }
    const root = rootRef.current;
    const menu = menuRef.current;
    if (!root || !menu) {
      return;
    }
    const getItems = () =>
      Array.from(menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not([disabled])'));
    (getItems()[0] ?? menu).focus({ preventScroll: true });

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.isComposing || !(event.target instanceof Node) || !root.contains(event.target)) {
        return;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
        toggleRef.current?.focus({ preventScroll: true });
        return;
      }
      const items = getItems();
      if (items.length === 0) {
        return;
      }
      const currentIndex = items.findIndex((item) => item === document.activeElement);
      let nextIndex: number | null = null;
      if (event.key === 'ArrowDown') {
        nextIndex = (currentIndex + 1 + items.length) % items.length;
      } else if (event.key === 'ArrowUp') {
        nextIndex = (currentIndex - 1 + items.length) % items.length;
      } else if (event.key === 'Home') {
        nextIndex = 0;
      } else if (event.key === 'End') {
        nextIndex = items.length - 1;
      }
      if (nextIndex !== null) {
        event.preventDefault();
        items[nextIndex]?.focus({ preventScroll: true });
      }
    };
    const handlePointerDown = (event: PointerEvent) => {
      if (event.target instanceof Node && !root.contains(event.target)) {
        onClose();
      }
    };
    const handleFocusIn = (event: FocusEvent) => {
      if (event.target instanceof Node && !root.contains(event.target)) {
        onClose();
      }
    };
    document.addEventListener('focusin', handleFocusIn);
    document.addEventListener('keydown', handleKeyDown);
    document.addEventListener('pointerdown', handlePointerDown);
    return () => {
      document.removeEventListener('focusin', handleFocusIn);
      document.removeEventListener('keydown', handleKeyDown);
      document.removeEventListener('pointerdown', handlePointerDown);
    };
  }, [isOpen, onClose]);
  return (
    <div className="km-overflow-menu" ref={rootRef}>
      <button
        aria-controls={isOpen ? menuId : undefined}
        aria-expanded={isOpen}
        aria-haspopup="menu"
        aria-label={t('workbench.overflow.label')}
        className="secondary-button icon-button"
        onClick={onToggle}
        ref={toggleRef}
        title={t('workbench.overflow.label')}
        type="button"
      >
        <MoreHorizontal aria-hidden="true" size={17} />
      </button>
      {isOpen ? (
        <div
          className="km-overflow-menu-items"
          id={menuId}
          ref={menuRef}
          role="menu"
          tabIndex={-1}
        >
          {actions.map((action) => (
            <button
              disabled={!action.isEnabled}
              key={action.id}
              onClick={() => {
                onExecute(action.id);
                onClose();
                toggleRef.current?.focus({ preventScroll: true });
              }}
              role="menuitem"
              title={action.disabledReasonKey ? t(action.disabledReasonKey) : undefined}
              type="button"
            >
              {t(action.labelKey)}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
