/* SPDX-License-Identifier: GPL-3.0-only */

import { CircleHelp } from 'lucide-react';
import {
  type CSSProperties,
  type KeyboardEvent,
  type ReactNode,
  isValidElement,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState
} from 'react';
import { createPortal } from 'react-dom';
import { useLocalization } from '../localization/LocalizationProvider';
import './ContextHelp.css';
import './Tooltip.css';

type ContextHelpPosition = {
  left: number;
  top: number;
};

export type ContextHelpProps = {
  children: ReactNode;
  className?: string;
  label: string;
};

const contextHelpEdgeGap = 12;
const contextHelpTriggerGap = 8;
const contextHelpCloseDelayMilliseconds = 140;

export function ContextHelp({ children, className, label }: ContextHelpProps) {
  const { t, translateLiteral } = useLocalization();
  const localizedLabel = translateLiteral(label);
  const localizedDescription = getContextHelpText(children, translateLiteral);
  const tooltipId = `context-help-${useId().replace(/:/g, '')}`;
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const tooltipRef = useRef<HTMLDivElement | null>(null);
  const closeTimerRef = useRef<number | null>(null);
  const isPinnedRef = useRef(false);
  const [isOpen, setIsOpen] = useState(false);
  const [position, setPosition] = useState<ContextHelpPosition>({ left: 0, top: 0 });
  const [placement, setPlacement] = useState<'above' | 'below'>('below');

  const clearCloseTimer = () => {
    if (closeTimerRef.current !== null) {
      window.clearTimeout(closeTimerRef.current);
      closeTimerRef.current = null;
    }
  };

  const openHelp = () => {
    clearCloseTimer();
    setIsOpen(true);
  };

  const closeHelp = () => {
    clearCloseTimer();
    isPinnedRef.current = false;
    setIsOpen(false);
  };

  const scheduleClose = () => {
    clearCloseTimer();
    if (isPinnedRef.current) {
      return;
    }

    closeTimerRef.current = window.setTimeout(() => {
      setIsOpen(false);
      closeTimerRef.current = null;
    }, contextHelpCloseDelayMilliseconds);
  };

  useEffect(
    () => () => {
      clearCloseTimer();
    },
    []
  );

  useEffect(() => {
    if (!isOpen) {
      return undefined;
    }

    const handlePointerDown = (event: globalThis.PointerEvent) => {
      const eventTarget = event.target as Node;
      if (
        !triggerRef.current?.contains(eventTarget) &&
        !tooltipRef.current?.contains(eventTarget)
      ) {
        closeHelp();
      }
    };

    const handleKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        closeHelp();
        triggerRef.current?.focus();
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen]);

  useLayoutEffect(() => {
    if (!isOpen || !triggerRef.current || !tooltipRef.current) {
      return undefined;
    }

    const updatePosition = () => {
      const triggerBounds = triggerRef.current?.getBoundingClientRect();
      const tooltipBounds = tooltipRef.current?.getBoundingClientRect();
      if (!triggerBounds || !tooltipBounds) {
        return;
      }

      const maximumLeft = Math.max(
        contextHelpEdgeGap,
        window.innerWidth - contextHelpEdgeGap - tooltipBounds.width
      );
      const centeredLeft =
        triggerBounds.left + triggerBounds.width / 2 - tooltipBounds.width / 2;
      const nextLeft = Math.min(
        Math.max(contextHelpEdgeGap, centeredLeft),
        maximumLeft
      );
      const roomBelow = window.innerHeight - triggerBounds.bottom - contextHelpTriggerGap;
      const roomAbove = triggerBounds.top - contextHelpTriggerGap;
      const shouldPlaceAbove =
        roomBelow < Math.min(tooltipBounds.height, 180) && roomAbove > roomBelow;
      const idealTop = shouldPlaceAbove
        ? triggerBounds.top - contextHelpTriggerGap - tooltipBounds.height
        : triggerBounds.bottom + contextHelpTriggerGap;
      const maximumTop = Math.max(
        contextHelpEdgeGap,
        window.innerHeight - contextHelpEdgeGap - tooltipBounds.height
      );
      const nextTop = Math.min(
        Math.max(contextHelpEdgeGap, idealTop),
        maximumTop
      );

      setPlacement(shouldPlaceAbove ? 'above' : 'below');
      setPosition({ left: nextLeft, top: nextTop });
    };

    updatePosition();
    const resizeObserver = new ResizeObserver(updatePosition);
    resizeObserver.observe(tooltipRef.current);
    resizeObserver.observe(triggerRef.current);
    window.addEventListener('resize', updatePosition);
    window.addEventListener('scroll', updatePosition, true);
    return () => {
      resizeObserver.disconnect();
      window.removeEventListener('resize', updatePosition);
      window.removeEventListener('scroll', updatePosition, true);
    };
  }, [isOpen]);

  const togglePinnedHelp = () => {
    clearCloseTimer();
    const nextPinnedState = !isPinnedRef.current || !isOpen;
    isPinnedRef.current = nextPinnedState;
    setIsOpen(nextPinnedState);
  };

  const handleTriggerKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      event.stopPropagation();
      togglePinnedHelp();
      return;
    }

    if (!tooltipRef.current || !isOpen) {
      return;
    }

    const scrollAmount = Math.max(48, tooltipRef.current.clientHeight * 0.7);
    if (event.key === 'PageDown' || event.key === 'ArrowDown') {
      event.preventDefault();
      tooltipRef.current.scrollBy({ top: scrollAmount });
    } else if (event.key === 'PageUp' || event.key === 'ArrowUp') {
      event.preventDefault();
      tooltipRef.current.scrollBy({ top: -scrollAmount });
    } else if (event.key === 'Home') {
      event.preventDefault();
      tooltipRef.current.scrollTo({ top: 0 });
    } else if (event.key === 'End') {
      event.preventDefault();
      tooltipRef.current.scrollTo({ top: tooltipRef.current.scrollHeight });
    }
  };

  return (
    <>
      <button
        aria-controls={`${tooltipId}-popover`}
        aria-describedby={`${tooltipId}-description`}
        aria-expanded={isOpen}
        aria-label={t('contextHelp.ariaLabel', { label: localizedLabel })}
        className={`context-help-trigger ${className ?? ''}`.trim()}
        onBlur={scheduleClose}
        onClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          togglePinnedHelp();
        }}
        onFocus={openHelp}
        onKeyDown={handleTriggerKeyDown}
        onPointerEnter={openHelp}
        onPointerLeave={scheduleClose}
        ref={triggerRef}
        title=""
        type="button"
      >
        <CircleHelp aria-hidden="true" size={15} strokeWidth={2.2} />
      </button>
      {typeof document === 'undefined'
        ? null
        : createPortal(
            <span className="context-help-screen-reader-text" id={`${tooltipId}-description`}>
              {localizedLabel}: {localizedDescription}
            </span>,
            document.body
          )}
      {isOpen
        ? createPortal(
            <div
              className="tooltip-surface tooltip-surface-rich context-help-popover"
              data-positioned="true"
              data-placement={placement}
              id={`${tooltipId}-popover`}
              onPointerEnter={clearCloseTimer}
              onPointerLeave={scheduleClose}
              ref={tooltipRef}
              role="tooltip"
              style={
                {
                  '--tooltip-left': `${position.left}px`,
                  '--tooltip-top': `${position.top}px`
                } as CSSProperties
              }
            >
              <strong className="context-help-title">{localizedLabel}</strong>
              <div className="context-help-body">{children}</div>
            </div>,
            document.body
          )
        : null}
    </>
  );
}

function getContextHelpText(node: ReactNode, translateLiteral: (literal: string) => string): string {
  if (typeof node === 'string' || typeof node === 'number') {
    return translateLiteral(String(node));
  }

  if (Array.isArray(node)) {
    return node
      .map((child) => getContextHelpText(child, translateLiteral))
      .filter(Boolean)
      .join(' ');
  }

  if (isValidElement<{ children?: ReactNode }>(node)) {
    return getContextHelpText(node.props.children, translateLiteral);
  }

  return '';
}
