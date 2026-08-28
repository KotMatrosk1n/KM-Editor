/* SPDX-License-Identifier: GPL-3.0-only */
OUTPUT_FORMAT(elf64-littleaarch64)
OUTPUT_ARCH(aarch64)
ENTRY(__module_start)

PHDRS
{
  text PT_LOAD FLAGS(5);
  rodata PT_LOAD FLAGS(4);
  data PT_LOAD FLAGS(6);
  dynamic PT_DYNAMIC FLAGS(6);
}

SECTIONS
{
  . = 0;
  .text : ALIGN(4) {
    KEEP(*(.text.crt0))
    *(.text .text.*)
  } :text

  .plt : { *(.plt .plt.*) } :text

  . = ALIGN(0x1000);
  .rodata : {
    KEEP(*(.rodata.mod0))
    *(.rodata .rodata.*)
  } :rodata

  .hash : { *(.hash) } :rodata
  .gnu.hash : { *(.gnu.hash) } :rodata
  .dynsym : { *(.dynsym .dynsym.*) } :rodata
  .dynstr : { *(.dynstr .dynstr.*) } :rodata
  .rela.dyn : { *(.rela.dyn .rela.dyn.*) } :rodata
  .rela.plt : { *(.rela.plt .rela.plt.*) } :rodata
  .note.gnu.build-id : { *(.note.gnu.build-id) } :rodata

  . = ALIGN(0x1000);
  .data : {
    *(.data .data.*)
    *(.data.rel.ro .data.rel.ro.*)
    *(.got .got.* .got.plt)
  } :data

  .dynamic : {
    HIDDEN(__dynamic_start__ = .);
    *(.dynamic)
  } :data :dynamic

  .init_array : {
    KEEP(*(.init_array .init_array.*))
  } :data

  . = ALIGN(0x1000);
  .bss (NOLOAD) : {
    HIDDEN(__bss_start__ = .);
    *(.bss .bss.*)
    *(COMMON)
    . = ALIGN(8);
    HIDDEN(__bss_end__ = .);
  } :data

  /DISCARD/ : {
    *(.interp)
    *(.comment)
    *(.eh_frame .eh_frame_hdr)
  }
}
