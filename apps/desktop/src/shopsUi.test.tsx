/* SPDX-License-Identifier: GPL-3.0-only */

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ApiDiagnostic, type EditSession, type ShopsWorkflow } from './bridge/contracts';
import { languageStorageKey, LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

const originalSession: EditSession = {
  hasPendingChanges: true,
  pendingEdits: [
    {
      domain: 'workflow.items',
      field: 'pouch',
      newValue: '4',
      recordId: '1',
      sources: [{ layer: 'base', relativePath: 'romfs/bin/pml/item/item.dat' }],
      summary: 'Existing shared edit.'
    }
  ],
  sessionId: 'session-1'
};

async function createShopsHarness(
  mutateWorkflow?: (workflow: ShopsWorkflow) => ShopsWorkflow,
  session: EditSession = originalSession
) {
  const bridge = createMockProjectBridge({}, true);
  const [{ workflow: loadedShopsWorkflow }, { workflow: itemsWorkflow }] = await Promise.all([
    bridge.loadShopsWorkflow({ paths: projectPaths }),
    bridge.loadItemsWorkflow({ paths: projectPaths })
  ]);
  const shopsWorkflow = mutateWorkflow?.(loadedShopsWorkflow) ?? loadedShopsWorkflow;

  useWorkbenchStore.setState({
    activeSection: 'shops',
    draftPaths: projectPaths,
    editSession: session,
    editValidationDiagnostics: [],
    itemsWorkflow,
    selectedItemId: null,
    selectedShopId: shopsWorkflow.shops[0]?.shopId ?? null,
    shopSearchText: '',
    shopsWorkflow
  });

  return { bridge, shopsWorkflow };
}

function renderShops(bridge: ReturnType<typeof createMockProjectBridge>) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

function withShopPrice(
  workflow: ShopsWorkflow,
  currency: string,
  field: string | null,
  price: number
): ShopsWorkflow {
  return {
    ...workflow,
    shops: workflow.shops.map((shop, index) =>
      index === 0
        ? {
            ...shop,
            currency,
            globalPriceField: field,
            inventory: shop.inventory.map((item, itemIndex) =>
              itemIndex === 0 ? { ...item, canEditPrice: true, price } : item
            )
          }
        : shop
    )
  };
}

function createRowFieldWorkflow(workflow: ShopsWorkflow, rowCount: number): ShopsWorkflow {
  const sourceShop = workflow.shops[0]!;
  const sourceItem = sourceShop.inventory[0]!;
  return {
    ...workflow,
    editableFields: [
      ...workflow.editableFields,
      {
        field: 'quantity',
        label: 'Quantity',
        maximumValue: 99,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }
    ],
    editorFamily: 'sv',
    shops: [
      {
        ...sourceShop,
        canEditInventoryOrder: false,
        editorFamily: 'sv',
        globalPriceField: null,
        inventory: Array.from({ length: rowCount }, (_, index) => ({
          ...sourceItem,
          canEditPrice: false,
          fieldDisplayValues: { quantity: (index + 1).toString() },
          fieldValues: { quantity: (index + 1).toString() },
          itemId: index + 1,
          itemName: `Item ${index + 1}`,
          priceField: 'price',
          rowId: `row-${index + 1}`,
          slot: index + 1,
          supportedFields: ['quantity']
        }))
      }
    ]
  };
}

async function editQuantity(user: ReturnType<typeof userEvent.setup>, slot: number, value: string) {
  const itemInput = screen.getByLabelText(`Shop slot ${slot} item`);
  const row = itemInput.closest('.shop-inventory-editor-row');
  expect(row).not.toBeNull();
  fireEvent.click(row!);
  const quantityInput = await screen.findByLabelText('Quantity');
  await user.clear(quantityInput);
  await user.type(quantityInput, value);
}

describe('Shops UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it.each([
    ['Money', 'buyPrice', 300],
    ['Watts', 'wattsPrice', 50],
    ['BP', 'alternatePrice', 5],
    ['Dynite Ore', 'alternatePrice', 5]
  ])('stages %s prices through the exact %s item field', async (currency, field, price) => {
    const user = userEvent.setup();
    const { bridge } = await createShopsHarness((workflow) =>
      withShopPrice(workflow, currency, field, price)
    );
    const updateItemFields = vi.spyOn(bridge, 'updateItemFields');
    renderShops(bridge);

    const priceInput = await screen.findByLabelText('Shop slot 1 price');
    expect(priceInput).toHaveValue(price);
    await user.clear(priceInput);
    await user.type(priceInput, (price + 1).toString());
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateItemFields).toHaveBeenCalledTimes(1));
    expect(updateItemFields.mock.calls[0]?.[0].updates).toEqual([
      { field, itemId: 1, value: (price + 1).toString() }
    ]);
  });

  it('disables global price editing when the backend cannot identify a price field', async () => {
    const { bridge } = await createShopsHarness((workflow) =>
      withShopPrice(workflow, 'Unknown currency', null, 300)
    );
    renderShops(bridge);

    expect(await screen.findByLabelText('Shop slot 1 price')).toBeDisabled();
  });

  it('preserves friendly-shop global price editing for S/V', async () => {
    const user = userEvent.setup();
    const { bridge } = await createShopsHarness((workflow) => ({
      ...workflow,
      editorFamily: 'sv',
      shops: workflow.shops.map((shop) => ({
        ...shop,
        editorFamily: 'sv',
        globalPriceField: 'buyPrice',
        inventory: shop.inventory.map((item) => ({
          ...item,
          canEditPrice: true,
          priceField: null
        }))
      }))
    }));
    const updateItemFields = vi.spyOn(bridge, 'updateItemFields');
    renderShops(bridge);

    const itemInput = await screen.findByLabelText('Shop slot 1 item');
    await user.clear(itemInput);
    await user.type(itemInput, 'Antidote{Enter}');

    expect(screen.getByLabelText('Shop slot 1 price')).toHaveValue(200);
    expect(screen.getByLabelText('Shop slot 1 price')).toBeEnabled();
    await user.clear(screen.getByLabelText('Shop slot 1 price'));
    await user.type(screen.getByLabelText('Shop slot 1 price'), '201');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateItemFields).toHaveBeenCalledTimes(1));
    expect(updateItemFields.mock.calls[0]?.[0].updates).toEqual([
      { field: 'buyPrice', itemId: 2, value: '201' }
    ]);
  });

  it('overlays a staged price only onto shops using the matching item field', async () => {
    const user = userEvent.setup();
    const { bridge } = await createShopsHarness((workflow) => {
      const wattShop = withShopPrice(workflow, 'Watts', 'wattsPrice', 50).shops[0]!;
      const moneyShop = {
        ...wattShop,
        currency: 'Money',
        globalPriceField: 'buyPrice',
        inventory: wattShop.inventory.map((item) => ({ ...item, price: 300 })),
        name: 'Money Shop',
        shopId: 'money-shop'
      };
      return {
        ...workflow,
        editableFields: [
          ...workflow.editableFields,
          {
            field: 'conditionKind',
            label: 'Condition kind',
            maximumValue: null,
            minimumValue: null,
            options: [
              {
                itemName: 'Condition 1',
                label: 'Condition 1',
                price: 777,
                prices: { buyPrice: 777 },
                value: 1
              }
            ],
            valueKind: 'integer' as const
          }
        ],
        shops: [wattShop, moneyShop]
      };
    });
    renderShops(bridge);

    const priceInput = await screen.findByLabelText('Shop slot 1 price');
    await user.clear(priceInput);
    await user.type(priceInput, '51');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => {
      const shops = useWorkbenchStore.getState().shopsWorkflow?.shops ?? [];
      expect(shops[0]?.inventory[0]?.price).toBe(51);
      expect(shops[1]?.inventory[0]?.price).toBe(300);
      const unrelatedOption = useWorkbenchStore
        .getState()
        .shopsWorkflow?.editableFields.find((field) => field.field === 'conditionKind')
        ?.options[0];
      expect(unrelatedOption?.price).toBe(777);
      expect(unrelatedOption?.prices).toEqual({ buyPrice: 777 });
    });
  });

  it('finds shops by their visible localized labels', async () => {
    const user = userEvent.setup();
    window.localStorage.setItem(languageStorageKey, 'es');
    const { bridge } = await createShopsHarness((workflow) => ({
      ...workflow,
      shops: workflow.shops.map((shop, index) =>
        index === 0 ? { ...shop, name: 'BP Shop' } : shop
      )
    }));
    renderShops(bridge);

    expect((await screen.findAllByText('Tienda de PB')).length).toBeGreaterThan(0);
    const search = screen.getByRole('searchbox');
    await user.type(search, 'Tienda de PB');

    expect(screen.getAllByText('Tienda de PB').length).toBeGreaterThan(0);
  });

  it('keeps a shared staged session when opening a shop item in Items', async () => {
    const user = userEvent.setup();
    const { bridge } = await createShopsHarness();
    renderShops(bridge);

    await user.click(await screen.findByRole('button', { name: 'Open Potion in Items' }));
    await user.click(screen.getByRole('button', { name: 'Open in Items' }));

    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('items'));
    expect(useWorkbenchStore.getState().editSession).toEqual(originalSession);
    expect(useWorkbenchStore.getState().selectedItemId).toBe(1);
  });

  it('stages row-field drafts retained across selection changes', async () => {
    const user = userEvent.setup();
    const { bridge, shopsWorkflow } = await createShopsHarness((workflow) =>
      createRowFieldWorkflow(workflow, 2)
    );
    const updateShopInventoryItem = vi.fn(async (request) => ({
      diagnostics: [],
      session: {
        hasPendingChanges: true,
        pendingEdits: [
          ...(request.session?.pendingEdits ?? []),
          {
            domain: 'workflow.shops',
            field: request.field,
            newValue: request.value,
            recordId: request.rowId ?? request.slot.toString(),
            sources: [{ layer: 'base' as const, relativePath: 'romfs/bin/shop.dat' }],
            summary: `Set ${request.field}.`
          }
        ],
        sessionId: request.session?.sessionId ?? 'session-1'
      },
      workflow: shopsWorkflow
    }));
    bridge.updateShopInventoryItem = updateShopInventoryItem;
    renderShops(bridge);

    await editQuantity(user, 1, '10');
    await editQuantity(user, 2, '20');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateShopInventoryItem).toHaveBeenCalledTimes(2));
    expect(updateShopInventoryItem.mock.calls.map(([request]) => ({
      slot: request.slot,
      value: request.value
    }))).toEqual([
      { slot: 1, value: '10' },
      { slot: 2, value: '20' }
    ]);
  });

  it('rolls back a middle rejection and retains all shop drafts for retry', async () => {
    const user = userEvent.setup();
    const { bridge, shopsWorkflow } = await createShopsHarness((workflow) =>
      createRowFieldWorkflow(workflow, 3)
    );
    const rejection: ApiDiagnostic = {
      domain: 'workflow.shops',
      message: 'The second row was rejected.',
      severity: 'error'
    };
    const warning: ApiDiagnostic = {
      domain: 'workflow.shops',
      message: 'The first row has a warning.',
      severity: 'warning'
    };
    const updateShopInventoryItem = vi.fn(async (request) => {
      const callNumber = updateShopInventoryItem.mock.calls.length;
      return {
        diagnostics: callNumber === 1 ? [warning] : callNumber === 2 ? [rejection] : [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            ...(request.session?.pendingEdits ?? []),
            {
              domain: 'workflow.shops',
              field: request.field,
              newValue: request.value,
              recordId: request.rowId ?? request.slot.toString(),
              sources: [{ layer: 'base' as const, relativePath: 'romfs/bin/shop.dat' }],
              summary: `Set ${request.field}.`
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-1'
        },
        workflow: shopsWorkflow
      };
    });
    bridge.updateShopInventoryItem = updateShopInventoryItem;
    renderShops(bridge);

    await editQuantity(user, 1, '10');
    await editQuantity(user, 2, '20');
    await editQuantity(user, 3, '30');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateShopInventoryItem).toHaveBeenCalledTimes(2));
    expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([warning, rejection]);
    expect(useWorkbenchStore.getState().editSession).toEqual(originalSession);
    expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled();
    expect(screen.getByLabelText('Quantity')).toHaveValue(30);
  });

  it('shows the first filtered shop instead of a hidden selection', async () => {
    const { bridge, shopsWorkflow } = await createShopsHarness((workflow) => {
      const sourceShop = workflow.shops[0]!;
      return {
        ...workflow,
        shops: [
          { ...sourceShop, name: 'Potion Mart', shopId: 'potion' },
          { ...sourceShop, name: 'Antidote Mart', shopId: 'antidote' }
        ]
      };
    });
    act(() => {
      useWorkbenchStore.setState({ selectedShopId: 'antidote', shopSearchText: 'Potion Mart' });
    });
    renderShops(bridge);

    expect(await screen.findByRole('searchbox', { name: 'Search shops' })).toHaveValue('Potion Mart');
    expect(screen.queryByText('Antidote Mart')).not.toBeInTheDocument();
    const visibleShopName = screen.getAllByText('Potion Mart')[0]!;
    expect(visibleShopName.closest('button')).toHaveAttribute('aria-selected', 'true');
    expect(useWorkbenchStore.getState().shopsWorkflow).toEqual(shopsWorkflow);
  });
});
