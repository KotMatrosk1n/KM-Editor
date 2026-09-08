// SPDX-License-Identifier: GPL-3.0-only
.section .text,"ax",%progbits
.align 2

.macro BEGIN name
    .global \name
    .hidden \name
    .type \name,%function
\name:
.endm
.macro CONTINUE name, target
    adrp x17, \target
    ldr x17, [x17, #:lo12:\target]
    br x17
    .size \name, . - \name
.endm

BEGIN km_swsh_options_ctor_original
    sub sp, sp, #0x60
    str x23, [sp, #0x20]
    stp x22, x21, [sp, #0x30]
    stp x20, x19, [sp, #0x40]
CONTINUE km_swsh_options_ctor_original, km_swsh_options_ctor_continue

BEGIN km_swsh_options_render_original
    sub sp, sp, #0xe0
    str d10, [sp, #0x60]
    stp d9, d8, [sp, #0x70]
    stp x28, x27, [sp, #0x80]
CONTINUE km_swsh_options_render_original, km_swsh_options_render_continue

BEGIN km_swsh_options_input_original
    sub sp, sp, #0xa0
    str x27, [sp, #0x40]
    stp x26, x25, [sp, #0x50]
    stp x24, x23, [sp, #0x60]
CONTINUE km_swsh_options_input_original, km_swsh_options_input_continue

BEGIN km_swsh_options_activate_original
    sub sp, sp, #0x60
    str x23, [sp, #0x20]
    stp x22, x21, [sp, #0x30]
    stp x20, x19, [sp, #0x40]
CONTINUE km_swsh_options_activate_original, km_swsh_options_activate_continue

BEGIN km_swsh_options_number_original
    stp x26, x25, [sp, #-0x50]!
    stp x24, x23, [sp, #0x10]
    stp x22, x21, [sp, #0x20]
    stp x20, x19, [sp, #0x30]
CONTINUE km_swsh_options_number_original, km_swsh_options_number_continue

.macro APPLY name, index
BEGIN \name
    // Entered by a branch at the stock BL site. Preserve its enclosing frame.
    str x30, [sp, #-16]!
    .if \index == 2
        bl km_swsh_options_reset_apply
    .else
        bl km_swsh_options_apply
    .endif
    ldr x30, [sp], #16
    cbz w0, 1f
    mov w8, #0x101
    .if \index == 0
        strh w8, [sp, #0x28]
        mov x8, #0x38f3
    .else
        stp xzr, xzr, [sp, #0x28]
        strh w8, [sp, #0x38]
    .endif
    adrp x17, km_swsh_options_apply_continue
    add x17, x17, #:lo12:km_swsh_options_apply_continue
    ldr x17, [x17, #(\index * 8)]
    br x17
1:
    mov w0, #1
    adrp x17, km_swsh_options_apply_failure
    add x17, x17, #:lo12:km_swsh_options_apply_failure
    ldr x17, [x17, #(\index * 8)]
    br x17
    .size \name, . - \name
.endm

APPLY km_swsh_options_apply_first, 0
APPLY km_swsh_options_apply_second, 1
APPLY km_swsh_options_reset_gate, 2
