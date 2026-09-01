/* SPDX-License-Identifier: GPL-3.0-only */

import {
  cloneElement,
  isValidElement,
  type CSSProperties,
  type FocusEvent,
  type KeyboardEvent,
  type PointerEvent,
  type ReactElement,
  type ReactNode,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState
} from 'react';
import { createPortal } from 'react-dom';
import { getEditorPortalHost } from './editorPortal';
import './Tooltip.css';

type HoverTooltipAnchorProps = {
  'aria-describedby'?: string;
  disabled?: boolean;
  onBlur?: (event: FocusEvent<HTMLElement>) => void;
  onFocus?: (event: FocusEvent<HTMLElement>) => void;
  onKeyDown?: (event: KeyboardEvent<HTMLElement>) => void;
  onPointerEnter?: (event: PointerEvent<HTMLElement>) => void;
  onPointerLeave?: (event: PointerEvent<HTMLElement>) => void;
  style?: CSSProperties;
  title?: string;
  type?: string;
};

type HoverTooltipPosition = {
  left: number;
  top: number;
};

export type HoverTooltipProps = {
  children: ReactElement;
  content?: ReactNode;
  describe?: boolean;
  detail?: 'simple' | 'full';
  placement?: 'auto' | 'above' | 'below';
};

const tooltipEdgeGap = 12;
const tooltipTriggerGap = 8;
const tooltipOpenDelayMilliseconds = 320;
const tooltipCloseDelayMilliseconds = 140;

export function HoverTooltip({
  children,
  content,
  describe = true,
  detail = 'simple',
  placement: placementPreference = 'auto'
}: HoverTooltipProps) {
  const tooltipId = `hover-tooltip-${useId().replace(/:/g, '')}`;
  const anchorRef = useRef<HTMLElement | null>(null);
  const tooltipRef = useRef<HTMLDivElement | null>(null);
  const openTimerRef = useRef<number | null>(null);
  const closeTimerRef = useRef<number | null>(null);
  const hasFocusRef = useRef(false);
  const [isOpen, setIsOpen] = useState(false);
  const [isPositioned, setIsPositioned] = useState(false);
  const [position, setPosition] = useState<HoverTooltipPosition>({ left: 0, top: 0 });
  const [resolvedPlacement, setResolvedPlacement] = useState<'above' | 'below'>('below');
  const anchorProps = children.props as HoverTooltipAnchorProps;
  const contentText = getTooltipText(content);
  const hasContent = contentText.trim().length > 0;

  const clearOpenTimer = () => {
    if (openTimerRef.current !== null) {
      window.clearTimeout(openTimerRef.current);
      openTimerRef.current = null;
    }
  };

  const clearCloseTimer = () => {
    if (closeTimerRef.current !== null) {
      window.clearTimeout(closeTimerRef.current);
      closeTimerRef.current = null;
    }
  };

  const openTooltip = (anchor: HTMLElement, immediately = false) => {
    anchorRef.current = anchor;
    clearCloseTimer();
    clearOpenTimer();
    if (!hasContent) {
      return;
    }

    if (immediately) {
      setIsPositioned(false);
      setIsOpen(true);
      return;
    }

    openTimerRef.current = window.setTimeout(() => {
      setIsPositioned(false);
      setIsOpen(true);
      openTimerRef.current = null;
    }, tooltipOpenDelayMilliseconds);
  };

  const closeTooltip = () => {
    clearOpenTimer();
    clearCloseTimer();
    setIsOpen(false);
  };

  const scheduleClose = () => {
    clearOpenTimer();
    clearCloseTimer();
    if (hasFocusRef.current) {
      return;
    }

    closeTimerRef.current = window.setTimeout(() => {
      setIsOpen(false);
      closeTimerRef.current = null;
    }, tooltipCloseDelayMilliseconds);
  };

  useEffect(
    () => () => {
      clearOpenTimer();
      clearCloseTimer();
    },
    []
  );

  useEffect(() => {
    if (hasContent) {
      return;
    }

    clearOpenTimer();
    clearCloseTimer();
    setIsOpen(false);
  }, [hasContent]);

  useEffect(() => {
    if (!isOpen) {
      return undefined;
    }

    const handlePointerDown = (event: globalThis.PointerEvent) => {
      const eventTarget = event.target as Node;
      if (
        !anchorRef.current?.contains(eventTarget) &&
        !tooltipRef.current?.contains(eventTarget)
      ) {
        closeTooltip();
      }
    };

    const handleKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        closeTooltip();
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
    if (!isOpen || !anchorRef.current || !tooltipRef.current) {
      return undefined;
    }

    const updatePosition = () => {
      const anchorBounds = anchorRef.current?.getBoundingClientRect();
      const tooltipBounds = tooltipRef.current?.getBoundingClientRect();
      if (!anchorBounds || !tooltipBounds) {
        return;
      }

      const maximumLeft = Math.max(
        tooltipEdgeGap,
        window.innerWidth - tooltipEdgeGap - tooltipBounds.width
      );
      const centeredLeft = anchorBounds.left + anchorBounds.width / 2 - tooltipBounds.width / 2;
      const nextLeft = Math.min(Math.max(tooltipEdgeGap, centeredLeft), maximumLeft);
      const roomBelow = window.innerHeight - anchorBounds.bottom - tooltipTriggerGap;
      const roomAbove = anchorBounds.top - tooltipTriggerGap;
      const minimumUsefulRoom = Math.min(tooltipBounds.height, 180);
      const shouldPlaceAbove =
        placementPreference === 'above'
          ? roomAbove >= Math.min(tooltipBounds.height, 80) || roomAbove > roomBelow
          : placementPreference === 'below'
            ? !(roomBelow >= Math.min(tooltipBounds.height, 80) || roomBelow > roomAbove)
            : roomBelow < minimumUsefulRoom && roomAbove > roomBelow;
      const idealTop = shouldPlaceAbove
        ? anchorBounds.top - tooltipTriggerGap - tooltipBounds.height
        : anchorBounds.bottom + tooltipTriggerGap;
      const maximumTop = Math.max(
        tooltipEdgeGap,
        window.innerHeight - tooltipEdgeGap - tooltipBounds.height
      );
      const nextTop = Math.min(Math.max(tooltipEdgeGap, idealTop), maximumTop);

      setResolvedPlacement(shouldPlaceAbove ? 'above' : 'below');
      setPosition({ left: nextLeft, top: nextTop });
      setIsPositioned(true);
    };

    updatePosition();
    const resizeObserver = new ResizeObserver(updatePosition);
    resizeObserver.observe(tooltipRef.current);
    resizeObserver.observe(anchorRef.current);
    window.addEventListener('resize', updatePosition);
    window.addEventListener('scroll', updatePosition, true);
    return () => {
      resizeObserver.disconnect();
      window.removeEventListener('resize', updatePosition);
      window.removeEventListener('scroll', updatePosition, true);
    };
  }, [isOpen, placementPreference]);

  if (!hasContent) {
    return cloneElement(children as ReactElement<HoverTooltipAnchorProps>, { title: undefined });
  }

  const describedBy = describe
    ? [anchorProps['aria-describedby'], `${tooltipId}-description`].filter(Boolean).join(' ')
    : anchorProps['aria-describedby'];
  const isDisabledNativeControl =
    anchorProps.disabled === true && typeof children.type === 'string';
  const isCompactDisabledControl =
    children.type === 'button' ||
    (children.type === 'input' &&
      (anchorProps.type === 'checkbox' || anchorProps.type === 'radio'));
  const anchor = isDisabledNativeControl ? (
    <span
      className={`hover-tooltip-disabled-anchor ${
        isCompactDisabledControl ? 'hover-tooltip-disabled-anchor-compact' : ''
      }`}
      onPointerEnter={(event) => {
        anchorProps.onPointerEnter?.(event);
        openTooltip(event.currentTarget);
      }}
      onPointerLeave={(event) => {
        anchorProps.onPointerLeave?.(event);
        scheduleClose();
      }}
    >
      {cloneElement(children as ReactElement<HoverTooltipAnchorProps>, {
        'aria-describedby': describedBy || undefined,
        style: { ...anchorProps.style, pointerEvents: 'none' },
        title: undefined
      })}
    </span>
  ) : (
    cloneElement(children as ReactElement<HoverTooltipAnchorProps>, {
      'aria-describedby': describedBy || undefined,
      onBlur: (event: FocusEvent<HTMLElement>) => {
        anchorProps.onBlur?.(event);
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) {
          return;
        }

        hasFocusRef.current = false;
        scheduleClose();
      },
      onFocus: (event: FocusEvent<HTMLElement>) => {
        anchorProps.onFocus?.(event);
        hasFocusRef.current = true;
        openTooltip(event.currentTarget, true);
      },
      onKeyDown: (event: KeyboardEvent<HTMLElement>) => {
        anchorProps.onKeyDown?.(event);
        if (event.key === 'Escape') {
          closeTooltip();
        }
      },
      onPointerEnter: (event: PointerEvent<HTMLElement>) => {
        anchorProps.onPointerEnter?.(event);
        openTooltip(event.currentTarget);
      },
      onPointerLeave: (event: PointerEvent<HTMLElement>) => {
        anchorProps.onPointerLeave?.(event);
        scheduleClose();
      },
      title: undefined
    })
  );
  const portalHost = getEditorPortalHost();

  return (
    <>
      {anchor}
      {describe ? (
        <span
          className="tooltip-screen-reader-text"
          hidden
          id={`${tooltipId}-description`}
        >
          {contentText}
        </span>
      ) : null}
      {isOpen && portalHost
        ? createPortal(
            <div
              className={`tooltip-surface tooltip-surface-${detail}`}
              data-placement={resolvedPlacement}
              data-positioned={isPositioned}
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
              {content}
            </div>,
            portalHost
          )
        : null}
    </>
  );
}

function getTooltipText(node: ReactNode): string {
  if (typeof node === 'string' || typeof node === 'number') {
    return String(node);
  }

  if (Array.isArray(node)) {
    return node.map(getTooltipText).filter(Boolean).join(' ');
  }

  if (isValidElement<{ children?: ReactNode }>(node)) {
    return getTooltipText(node.props.children);
  }

  return '';
}
