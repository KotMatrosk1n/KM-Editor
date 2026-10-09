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

BEGIN km_swsh_camera_init_original
    sub sp, sp, #0x80
    stp x20, x19, [sp, #0x60]
    stp x29, x30, [sp, #0x70]
    add x29, sp, #0x70
CONTINUE km_swsh_camera_init_original, km_swsh_camera_init_continue

BEGIN km_swsh_camera_update_original
    str x19, [sp, #-0x20]!
    stp x29, x30, [sp, #0x10]
    add x29, sp, #0x10
    ldr x8, [x0, #0x50]
CONTINUE km_swsh_camera_update_original, km_swsh_camera_update_continue

BEGIN km_swsh_camera_push_original
    str x23, [sp, #-0x40]!
    stp x22, x21, [sp, #0x10]
    stp x20, x19, [sp, #0x20]
    stp x29, x30, [sp, #0x30]
CONTINUE km_swsh_camera_push_original, km_swsh_camera_push_continue

BEGIN km_swsh_camera_queue_original
    str x23, [sp, #-0x40]!
    stp x22, x21, [sp, #0x10]
    stp x20, x19, [sp, #0x20]
    stp x29, x30, [sp, #0x30]
CONTINUE km_swsh_camera_queue_original, km_swsh_camera_queue_continue
