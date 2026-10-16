#!/usr/bin/env python3
import argparse, struct
from pathlib import Path

def align(v,a): return (v + a - 1) // a * a

def parse_ico(path):
    b=Path(path).read_bytes()
    reserved,typ,count=struct.unpack_from('<HHH',b,0)
    if reserved!=0 or typ!=1 or count<1: raise ValueError('not an icon file')
    icons=[]
    for i in range(count):
        o=6+i*16
        w,h,cc,res,planes,bpp,size,off=struct.unpack_from('<BBBBHHII',b,o)
        if off+size>len(b): raise ValueError('truncated ICO')
        icons.append(dict(width=w,height=h,color=cc,res=res,planes=planes,bpp=bpp,size=size,data=b[off:off+size]))
    return icons

def make_group(icons):
    out=bytearray(struct.pack('<HHH',0,1,len(icons)))
    for i,ic in enumerate(icons,1):
        out += struct.pack('<BBBBHHIH',ic['width'],ic['height'],ic['color'],ic['res'],ic['planes'],ic['bpp'],ic['size'],i)
    return bytes(out)

def build_rsrc(icons, base_rva, lang=0x0409):
    # Reserve directory tree and data-entry records first; append resource blobs later.
    b=bytearray()
    def pad4():
        while len(b)%4: b.append(0)
    def dir_header(n):
        off=len(b); b.extend(struct.pack('<IIHHHH',0,0,0,0,0,n)); return off
    def entry(idv, target_off, is_dir):
        b.extend(struct.pack('<II',idv, target_off | (0x80000000 if is_dir else 0)))
    def patch_entry(pos,idv,target_off,is_dir):
        struct.pack_into('<II',b,pos,idv,target_off | (0x80000000 if is_dir else 0))

    # root with RT_ICON (3), RT_GROUP_ICON (14)
    root=dir_header(2)
    root_entries=len(b); b.extend(b'\0'*16)

    # RT_ICON type directory: IDs 1..N
    icon_type=len(b); dir_header(len(icons)); icon_type_entries=len(b); b.extend(b'\0'*(8*len(icons)))
    icon_lang_dirs=[]
    for _ in icons:
        off=len(b); dir_header(1); ep=len(b); b.extend(b'\0'*8); icon_lang_dirs.append((off,ep))

    # RT_GROUP_ICON type directory: ID 1
    group_type=len(b); dir_header(1); group_type_entry=len(b); b.extend(b'\0'*8)
    group_lang=len(b); dir_header(1); group_lang_entry=len(b); b.extend(b'\0'*8)
    pad4()

    # Data entries
    icon_data_entries=[]
    for _ in icons:
        off=len(b); b.extend(b'\0'*16); icon_data_entries.append(off)
    group_data_entry=len(b); b.extend(b'\0'*16)
    pad4()

    # Patch directory entries
    patch_entry(root_entries,3,icon_type,True)
    patch_entry(root_entries+8,14,group_type,True)
    for i,(langdir,langentry) in enumerate(icon_lang_dirs,1):
        patch_entry(icon_type_entries+(i-1)*8,i,langdir,True)
        patch_entry(langentry,lang,icon_data_entries[i-1],False)
    patch_entry(group_type_entry,1,group_lang,True)
    patch_entry(group_lang_entry,lang,group_data_entry,False)

    # Resource data payloads and IMAGE_RESOURCE_DATA_ENTRY records
    for i,ic in enumerate(icons):
        pad4(); data_off=len(b); b.extend(ic['data'])
        struct.pack_into('<IIII',b,icon_data_entries[i],base_rva+data_off,len(ic['data']),0,0)
    group=make_group(icons)
    pad4(); group_off=len(b); b.extend(group)
    struct.pack_into('<IIII',b,group_data_entry,base_rva+group_off,len(group),0,0)
    pad4()
    return bytes(b)

def patch_pe_icon(exe_path, ico_path, out_path):
    data=bytearray(Path(exe_path).read_bytes())
    pe=struct.unpack_from('<I',data,0x3c)[0]
    if data[pe:pe+4]!=b'PE\0\0': raise ValueError('not PE')
    nsec=struct.unpack_from('<H',data,pe+6)[0]
    optsz=struct.unpack_from('<H',data,pe+20)[0]
    opt=pe+24
    magic=struct.unpack_from('<H',data,opt)[0]
    if magic!=0x20b: raise ValueError('expected PE32+')
    sec_align=struct.unpack_from('<I',data,opt+32)[0]
    file_align=struct.unpack_from('<I',data,opt+36)[0]
    size_headers=struct.unpack_from('<I',data,opt+60)[0]
    dd=opt+112
    sec_table=opt+optsz
    new_sh=sec_table+nsec*40
    if new_sh+40>size_headers: raise ValueError('no room for another section header')
    # Refuse existing resources to avoid orphaning an old tree.
    old_rva,old_sz=struct.unpack_from('<II',data,dd+2*8)
    if old_rva or old_sz: raise ValueError('PE already has resources')

    max_end_va=0
    max_end_raw=0
    for i in range(nsec):
        o=sec_table+i*40
        vs,va,rawsz,rawptr=struct.unpack_from('<IIII',data,o+8)
        max_end_va=max(max_end_va, va+max(vs,rawsz))
        max_end_raw=max(max_end_raw, rawptr+rawsz)
    new_va=align(max_end_va,sec_align)
    rawptr=align(max(len(data),max_end_raw),file_align)
    icons=parse_ico(ico_path)
    rsrc=build_rsrc(icons,new_va)
    rawsz=align(len(rsrc),file_align)
    virt=len(rsrc)

    if len(data)<rawptr: data.extend(b'\0'*(rawptr-len(data)))
    data.extend(rsrc)
    if len(rsrc)<rawsz: data.extend(b'\0'*(rawsz-len(rsrc)))

    # New IMAGE_SECTION_HEADER
    name=b'.rsrc\0\0\0'
    sh=struct.pack('<8sIIIIIIHHI',name,virt,new_va,rawsz,rawptr,0,0,0,0,0x40000040)
    data[new_sh:new_sh+40]=sh
    struct.pack_into('<H',data,pe+6,nsec+1)
    # SizeOfInitializedData
    old_init=struct.unpack_from('<I',data,opt+8)[0]
    struct.pack_into('<I',data,opt+8,old_init+rawsz)
    struct.pack_into('<I',data,opt+56,align(new_va+virt,sec_align)) # SizeOfImage
    struct.pack_into('<II',data,dd+2*8,new_va,virt) # Resource data directory
    # PE checksum left as zero/unchanged; Windows loader does not require it for normal EXEs.
    Path(out_path).write_bytes(data)

if __name__=='__main__':
    ap=argparse.ArgumentParser()
    ap.add_argument('exe'); ap.add_argument('ico'); ap.add_argument('out')
    a=ap.parse_args(); patch_pe_icon(a.exe,a.ico,a.out)
