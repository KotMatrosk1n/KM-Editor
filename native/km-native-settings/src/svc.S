// SPDX-License-Identifier: GPL-3.0-only

.macro KM_SVC_BEGIN name
    .section .text.\name,"ax",%progbits
    .global \name
    .hidden \name
    .type \name,%function
    .align 2
\name:
.endm

.macro KM_SVC_END name
    .size \name, . - \name
.endm

KM_SVC_BEGIN km_svc_query_memory
    str x1, [sp, #-16]!
    svc #0x06
    ldr x2, [sp], #16
    str w1, [x2]
    ret
KM_SVC_END km_svc_query_memory

KM_SVC_BEGIN km_svc_get_info
    str x0, [sp, #-16]!
    svc #0x29
    ldr x2, [sp], #16
    str x1, [x2]
    ret
KM_SVC_END km_svc_get_info

KM_SVC_BEGIN km_svc_map_process_memory
    svc #0x74
    ret
KM_SVC_END km_svc_map_process_memory

KM_SVC_BEGIN km_svc_unmap_process_memory
    svc #0x75
    ret
KM_SVC_END km_svc_unmap_process_memory

KM_SVC_BEGIN km_svc_create_thread
    str x0, [sp, #-16]!
    svc #0x08
    ldr x2, [sp], #16
    str w1, [x2]
    ret
KM_SVC_END km_svc_create_thread

KM_SVC_BEGIN km_svc_start_thread
    svc #0x09
    ret
KM_SVC_END km_svc_start_thread

KM_SVC_BEGIN km_svc_exit_thread
    svc #0x0A
1:
    b 1b
KM_SVC_END km_svc_exit_thread

KM_SVC_BEGIN km_svc_sleep_thread
    svc #0x0B
    ret
KM_SVC_END km_svc_sleep_thread

KM_SVC_BEGIN km_svc_close_handle
    svc #0x16
    ret
KM_SVC_END km_svc_close_handle

KM_SVC_BEGIN km_svc_wait_synchronization
    str x0, [sp, #-16]!
    svc #0x18
    ldr x2, [sp], #16
    str w1, [x2]
    ret
KM_SVC_END km_svc_wait_synchronization

KM_SVC_BEGIN km_svc_send_sync_request
    svc #0x21
    ret
KM_SVC_END km_svc_send_sync_request

KM_SVC_BEGIN km_svc_create_session
    stp x0, x1, [sp, #-16]!
    svc #0x40
    ldp x3, x4, [sp], #16
    str w1, [x3]
    str w2, [x4]
    ret
KM_SVC_END km_svc_create_session

KM_SVC_BEGIN km_svc_reply_and_receive
    str x0, [sp, #-16]!
    svc #0x43
    ldr x2, [sp], #16
    str w1, [x2]
    ret
KM_SVC_END km_svc_reply_and_receive

KM_SVC_BEGIN km_get_thread_local_region
    mrs x0, tpidrro_el0
    ret
KM_SVC_END km_get_thread_local_region

KM_SVC_BEGIN km_flush_data_cache
    cbz x1, 2f
    add x4, x0, x1
    mrs x2, ctr_el0
    ubfx x2, x2, #16, #4
    mov x3, #4
    lsl x2, x3, x2
    sub x3, x2, #1
    bic x0, x0, x3
    sub x4, x4, #1
    bic x4, x4, x3
1:
    // Publish continuation cache lines before the entry cache line. Together
    // with the runtime's entry-word-last stores, a multi-line hook cannot make
    // its entry executable before its complete continuation is at the PoC.
    dc civac, x4
    cmp x4, x0
    b.eq 3f
    sub x4, x4, x2
    b 1b
3:
    dsb ish
2:
    ret
KM_SVC_END km_flush_data_cache

KM_SVC_BEGIN km_invalidate_instruction_cache
    cbz x1, 2f
    add x4, x0, x1
    mrs x2, ctr_el0
    and x2, x2, #0xf
    mov x3, #4
    lsl x2, x3, x2
    sub x3, x2, #1
    bic x0, x0, x3
1:
    ic ivau, x0
    add x0, x0, x2
    cmp x0, x4
    b.lo 1b
    dsb ish
    isb
2:
    ret
KM_SVC_END km_invalidate_instruction_cache
