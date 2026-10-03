// SPDX-License-Identifier: GPL-3.0-only
// Linked into exact, reserved return padding by the layout generator.
// Descriptor metadata: hidden[0:10], initial[11:14] (15 = retail),
// packed shift[15:19], width[20:23], inverted[24], version[25:31].

initial:
    stp x9, x10, [sp, #-80]!
    mov w9, wzr
    b normalize
loaded:
    stp x9, x10, [sp, #-80]!
    mov w8, w10
    mov w9, #1
normalize:
    stp x11, x12, [sp, #16]
    stp x13, x14, [sp, #32]
    stp x15, x16, [sp, #48]
    stp x17, x18, [sp, #64]
    adrp x10, descriptors
    add x10, x10, :lo12:descriptors
    mov w11, wzr
normalize_row:
    ldr w12, [x10, #52]
    ubfx w14, w12, #15, #5
    ubfx w15, w12, #20, #4
    mov w17, #1
    lsl w15, w17, w15
    sub w15, w15, #1
    lsr w13, w8, w14
    and w13, w13, w15
    cmp w11, #1
    b.ne normalize_language_ok
    cmp w13, #1
    b.hi normalize_next
normalize_language_ok:
    ubfx w17, w12, #24, #1
    eor w13, w13, w17
    cbz w9, normalize_initial
    lsr w16, w12, w13
    tbz w16, #0, normalize_next
normalize_initial:
    ubfx w16, w12, #11, #4
    cmp w16, #15
    b.eq normalize_next
    eor w16, w16, w17
    lsl w16, w16, w14
    lsl w15, w15, w14
    bic w8, w8, w15
    orr w8, w8, w16
normalize_next:
    add x10, x10, #56
    add w11, w11, #1
    cmp w11, #16
    b.lo normalize_row
    cbnz w9, store_loaded
    str w8, [x19, #96]
    b normalize_return
store_loaded:
    str w8, [x19]
normalize_return:
    ldp x11, x12, [sp, #16]
    ldp x13, x14, [sp, #32]
    ldp x15, x16, [sp, #48]
    ldp x17, x18, [sp, #64]
    ldp x9, x10, [sp], #80
    ret

// x0 = view, w1 = requested value, w2 = descriptor ID. The retail caller
// reads the updated descriptor for previews, including text and audio.
navigate:
    cmp w2, #16
    b.hs navigate_return
    adrp x8, descriptors
    add x8, x8, :lo12:descriptors
    mov w9, #56
    umaddl x8, w2, w9, x8
    ldr w12, [x8, #52]
    and w12, w12, #0x7ff
    cbz w12, navigate_return
    // Both retail and the native Gameplay Settings view use this store.
    // Its verified immediate follows the relocated descriptor array.
    adrp x8, value_store
    ldr w8, [x8, :lo12:value_store]
    ubfx w8, w8, #10, #12
    add x8, x0, x8, lsl #2
    umaddl x8, w2, w9, x8
    ldr w10, [x8]
    cmp w1, w10
    b.eq navigate_return
    mov w11, #1
    cneg w11, w11, lo
    mov w13, #1
    cmp w2, #0
    cinc w13, w13, eq
    cmp w2, #13
    mov w14, #10
    csel w13, w14, w13, hs
navigate_choice:
    cmp w1, w13
    b.hi navigate_restore
    lsr w14, w12, w1
    tbz w14, #0, navigate_return
    add w1, w1, w11
    b navigate_choice
navigate_restore:
    mov w1, w10
navigate_return:
    sub sp, sp, #32
    b value_continue
