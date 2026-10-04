/* SPDX-License-Identifier: GPL-3.0-only */
import { type HTMLAttributes, type ReactNode, useCallback, useLayoutEffect, useRef, useState } from 'react';
import { type ReactVirtualizerOptions, useVirtualizer } from '@tanstack/react-virtual';

const virtualTableInitialRect = { height: 480, width: 800 };
const virtualTableOverscan = 8;
const virtualTableRowHeight = 40;

function calculateVirtualTableScrollMargin({
  bodyTop,
  clientTop,
  scrollTop,
  scrollViewportTop
}: {
  bodyTop: number;
  clientTop: number;
  scrollTop: number;
  scrollViewportTop: number;
}) {
  return Math.max(0, bodyTop - scrollViewportTop - clientTop + scrollTop);
}
const observeVirtualTableElementRect:
  | ReactVirtualizerOptions<HTMLDivElement, HTMLDivElement>['observeElementRect']
  | undefined =
  typeof ResizeObserver === 'undefined'
    ? (_instance, callback) => {
        callback(virtualTableInitialRect);
        return () => undefined;
      }
    : undefined;

type InteractiveTableRowProps = HTMLAttributes<HTMLDivElement> & {
  disabled?: boolean;
};

export function InteractiveTableRow({
  'aria-disabled': ariaDisabled,
  className,
  disabled = false,
  onClick,
  onKeyDown,
  tabIndex,
  ...rowProps
}: InteractiveTableRowProps) {
  const isDisabled = disabled || ariaDisabled === true || ariaDisabled === 'true';

  return (
    <div
      {...rowProps}
      aria-disabled={isDisabled || undefined}
      className={`${className ?? ''} interactive-table-row`.trim()}
      onClick={isDisabled ? undefined : onClick}
      onKeyDown={(event) => {
        onKeyDown?.(event);
        if (
          isDisabled ||
          event.defaultPrevented ||
          event.nativeEvent.isComposing ||
          (event.key !== 'Enter' && event.key !== ' ')
        ) {
          return;
        }

        event.preventDefault();
        event.currentTarget.click();
      }}
      role="row"
      tabIndex={isDisabled ? -1 : (tabIndex ?? 0)}
    />
  );
}

export function VirtualTableBody<T>({
  estimateSize = virtualTableRowHeight,
  getKey,
  items,
  measureRows = false,
  renderRow,
  resetKey
}: {
  estimateSize?: number;
  getKey: (item: T, index: number) => string | number;
  items: T[];
  measureRows?: boolean;
  renderRow: (item: T, index: number) => ReactNode;
  resetKey?: string | number;
}) {
  const bodyRef = useRef<HTMLDivElement | null>(null);
  const [scrollMargin, setScrollMargin] = useState(0);
  const getScrollElement = useCallback(() => {
    const tableElement = bodyRef.current?.parentElement;
    return tableElement instanceof HTMLDivElement ? tableElement : null;
  }, []);
  const rowVirtualizer = useVirtualizer({
    count: items.length,
    estimateSize: () => estimateSize,
    getItemKey: (index) => getKey(items[index]!, index),
    getScrollElement,
    initialRect: virtualTableInitialRect,
    overscan: virtualTableOverscan,
    scrollMargin,
    ...(observeVirtualTableElementRect
      ? { observeElementRect: observeVirtualTableElementRect }
      : {})
  });

  useLayoutEffect(() => {
    const bodyElement = bodyRef.current;
    const scrollElement = getScrollElement();
    if (!bodyElement || !scrollElement) {
      return undefined;
    }

    const updateScrollMargin = () => {
      const bodyRect = bodyElement.getBoundingClientRect();
      const scrollRect = scrollElement.getBoundingClientRect();
      const nextMargin = calculateVirtualTableScrollMargin({
        bodyTop: bodyRect.top,
        clientTop: scrollElement.clientTop,
        scrollTop: scrollElement.scrollTop,
        scrollViewportTop: scrollRect.top
      });
      setScrollMargin((currentMargin) =>
        currentMargin === nextMargin ? currentMargin : nextMargin
      );
    };
    updateScrollMargin();

    const resizeObserver = typeof ResizeObserver === 'undefined'
      ? null
      : new ResizeObserver(updateScrollMargin);
    resizeObserver?.observe(scrollElement);
    resizeObserver?.observe(bodyElement);
    const headingElement = bodyElement.previousElementSibling;
    if (headingElement instanceof HTMLElement) {
      resizeObserver?.observe(headingElement);
    }
    window.addEventListener('resize', updateScrollMargin);

    return () => {
      resizeObserver?.disconnect();
      window.removeEventListener('resize', updateScrollMargin);
    };
  }, [getScrollElement]);

  useLayoutEffect(() => {
    if (resetKey === undefined) {
      return;
    }

    const scrollElement = getScrollElement();
    if (scrollElement) {
      scrollElement.scrollTop = 0;
      scrollElement.scrollLeft = 0;
    }
    rowVirtualizer.scrollToOffset(0);
  }, [getScrollElement, resetKey]);

  return (
    <div className="virtual-table-body" ref={bodyRef} role="rowgroup">
      <div
        className="virtual-table-spacer"
        style={{ height: `${rowVirtualizer.getTotalSize()}px` }}
      >
        {rowVirtualizer.getVirtualItems().map((virtualRow) => {
          const item = items[virtualRow.index];

          if (item === undefined) {
            return null;
          }

          return (
            <div
              className="virtual-table-row"
              data-index={virtualRow.index}
              key={virtualRow.key}
              ref={measureRows ? rowVirtualizer.measureElement : undefined}
              role="presentation"
              style={{
                ...(measureRows ? {} : { height: `${virtualRow.size}px` }),
                transform: `translateY(${virtualRow.start - scrollMargin}px)`
              }}
            >
              {renderRow(item, virtualRow.index)}
            </div>
          );
        })}
      </div>
    </div>
  );
}
