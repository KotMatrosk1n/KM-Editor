// SPDX-License-Identifier: GPL-3.0-only
// Species and form arrive in w1 and w2. The original frame has eight unused bytes.
entry:
    sub sp, sp, #0x40
    orr w16, w1, w2, lsl #16
    adrp x17, table
    add x17, x17, :lo12:table
lookup:
    ldr w0, [x17]
    cbz w0, missing
    cmp w0, w16
    b.eq found
    ldrh w0, [x17, #10]
    cbz w0, missing
    add x17, x17, x0, lsl #2
    b lookup
found:
    add x17, x17, #4
    b ready
missing:
    mov x17, xzr
ready:
    str x17, [sp]
    b original_entry
equal_items:
    ldr x17, [sp]
    cbnz x17, random
    b.ne random
    b original_return
random:
    b original_random
rate_pointer:
    ldr x13, [sp]
    cbnz x13, rates_ready
    adrp x13, global_rates
    add x13, x13, :lo12:global_rates
rates_ready:
    b original_rates
