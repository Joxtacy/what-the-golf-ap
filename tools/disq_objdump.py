import re, subprocess, sys, os

DLL = r"C:/Program Files (x86)/Steam/steamapps/common/WHAT THE GOLF/GameAssembly.dll"
DUMP = r"C:/Users/Joxtacy/ap-build/dump/dump.cs"
IMAGE_BASE = 0x180000000
OBJDUMP = "/c/msys64/mingw64/bin/objdump.exe"

data = open(DLL, "rb").read()

rva_name = {}
name_re = re.compile(r"// RVA: (0x[0-9A-Fa-f]+) Offset: (0x[0-9A-Fa-f]+)")
lines = open(DUMP, encoding="utf-8", errors="replace").read().splitlines()
for i, l in enumerate(lines):
    m = name_re.search(l)
    if not m:
        continue
    rva = int(m.group(1), 16)
    for j in range(i + 1, min(i + 4, len(lines))):
        s = lines[j].strip()
        if s and not s.startswith("//") and "{" in s:
            rva_name[rva] = s.rstrip("{ ").strip()
            break

def disq(name, foff, va, length):
    slc = data[foff:foff + length]
    tmp = os.path.join(os.path.dirname(__file__), "_slice.bin")
    open(tmp, "wb").write(slc)
    out = subprocess.run(
        [OBJDUMP, "-D", "-b", "binary", "-mi386:x86-64", "-Mintel",
         f"--adjust-vma={hex(va)}", tmp],
        capture_output=True, text=True).stdout
    print(f"\n==== {name}  VA={hex(va)} off={hex(foff)} len={hex(length)} ====")
    for line in out.splitlines():
        m = re.match(r"\s+([0-9a-f]+):\s+(?:[0-9a-f]{2} )+\s*(.*)", line)
        if not m:
            continue
        addr = int(m.group(1), 16)
        asm = m.group(2).strip()
        tail = ""
        cm = re.search(r"\b(call|jmp)\s+0x([0-9a-f]+)", asm)
        if cm:
            tgt = int(cm.group(2), 16)
            nm = rva_name.get(tgt - IMAGE_BASE)
            if nm:
                tail = f"    ; -> {nm}"
        print(f"  {addr:#x}: {asm}{tail}")

for spec in sys.argv[1:]:
    nm, foff, va, length = spec.split(",")
    disq(nm, int(foff, 16), int(va, 16), int(length, 16))
