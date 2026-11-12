# -*- coding: utf-8 -*-
"""CTRL 3D • núcleo: banco de dados, cálculos e leitura dos arquivos do fatiador (sem interface)."""
import os
import io
import re
import sys
import math
import json
import zlib
import struct
import shutil
import sqlite3
import zipfile
import datetime as dt
import xml.etree.ElementTree as ET

CY, MG, YE, BK = "#00AEEF", "#EC008C", "#FFD200", "#111111"

STATUS = ["Orçamento", "Aprovado", "Em produção", "Pronto", "Entregue", "Pago", "Cancelado"]
STATUS_BAIXA = {"Em produção", "Pronto", "Entregue", "Pago"}
CATEGORIAS = ["Filamento", "Energia elétrica", "Peças / manutenção", "Embalagem", "Aluguel",
              "Internet / telefone", "Ferramentas", "Marketing", "Impostos / MEI", "Frete", "Outros"]
TIPOS = ["PLA", "PLA Silk", "PLA Matte", "PETG", "TPU", "ABS", "ASA", "Outro"]

# chave, rótulo, padrão, unidade, dica
CFG = [
    ("kwh_preco", "Preço do kWh (conta de luz)", 0.95, "R$/kWh", "Divida o total da conta pelos kWh consumidos."),
    ("watts", "Consumo médio da impressora", 250, "W",
     "Kobra X: máx. 1450 W (aquecendo). Imprimindo gasta bem menos; meça com uma tomada medidora."),
    ("maquina_valor", "Valor pago na impressora", 5000, "R$", "Usado para calcular o desgaste por hora."),
    ("vida_horas", "Vida útil estimada da impressora", 6000, "horas", ""),
    ("manut_hora", "Manutenção (bico, PTFE, mesa, correias)", 0.30, "R$/hora", ""),
    ("falha_pct", "Reserva para falhas / reimpressão", 10, "%", "Peças que dão errado também custam."),
    ("hora_trabalho", "Valor da sua hora de trabalho", 25, "R$/hora", ""),
    ("margem", "Margem de lucro padrão", 100, "%", "100% = preço é o dobro do custo."),
    ("imposto_pct", "Imposto sobre a venda", 0, "%", "MEI paga valor fixo: deixe 0 e lance o DAS em Gastos."),
    ("preco_kg_padrao", "Preço padrão do filamento", 100, "R$/kg", "Usado quando não escolher um rolo do estoque."),
    ("horas_dia", "Horas que a impressora roda por dia", 12, "h/dia", "Usado para calcular o prazo em dias."),
    ("troca_min", "Tempo para tirar a mesa e reiniciar", 5, "min", "Entre uma impressão e outra."),
    ("aquecer_min", "Aquecer e nivelar a cada impressão", 4, "min", "O fatiador não conta esse tempo."),
    ("mesa_x", "Largura útil da mesa", 260, "mm", "Kobra X: 260 × 260 mm."),
    ("mesa_y", "Profundidade útil da mesa", 260, "mm", ""),
    ("mesa_z", "Altura máxima de impressão", 260, "mm", ""),
    ("espaco_mm", "Espaço de segurança entre as peças", 5, "mm", "Folga entre uma peça e outra na mesa."),
    ("borda_mm", "Margem de segurança na borda da mesa", 5, "mm", "Espaço livre em volta, longe da beirada."),
    ("embalagem", "Embalagem padrão por peça", 1.00, "R$", ""),
    ("aviso_g", "Avisar quando o rolo tiver menos de", 150, "g", ""),
]

# ----------------------------------------------------------------- utilidades
def recurso(rel):
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, rel)


def caminho_db():
    if os.environ.get("CTRL3D_DB"):
        return os.environ["CTRL3D_DB"]
    return os.path.join(pasta_raiz(), "Dados", "ctrl3d_dados.db")


def pasta_raiz():
    """Pasta principal 'CTRL 3D' (onde fica o CTRL 3D.exe; o código-fonte fica em Codigo-fonte\\)."""
    if getattr(sys, "frozen", False):
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def num(s, padrao=0.0):
    try:
        s = str(s).strip().replace("R$", "").replace(" ", "")
        if not s:
            return padrao
        if "," in s:
            s = s.replace(".", "").replace(",", ".")
        return float(s)
    except (ValueError, TypeError):
        return padrao


def _br(txt):
    return txt.replace(",", "X").replace(".", ",").replace("X", ".")


def brl(v):
    return ("-" if v < 0 else "") + "R$ " + _br(f"{abs(v):,.2f}")


def fnum(v, casas=1):
    return _br(f"{v:,.{casas}f}")


def fhoras(h):
    total = int(round(h * 60))
    return f"{total // 60}h {total % 60:02d}min"


def fpeso(g):
    if g >= 1000:
        return f"{fnum(g / 1000, 2)} kg"
    if g < 0.005:
        return "0 g"
    return f"{fnum(g, 0 if g >= 100 else 1 if g >= 1 else 2)} g"


def hoje():
    return dt.date.today()


def data_br(iso):
    try:
        return dt.date.fromisoformat(iso).strftime("%d/%m/%Y")
    except (ValueError, TypeError):
        return iso or ""


def data_iso(br):
    return dt.datetime.strptime(br.strip(), "%d/%m/%Y").date().isoformat()


def arredonda_preco(v):
    passo = 0.5 if v >= 10 else 0.1
    return math.ceil(v / passo - 1e-9) * passo


def mes_br(ym):
    return f"{ym[5:7]}/{ym[:4]}"


def escurecer(hexc, f=0.82):
    r, g, b = (int(hexc[i:i + 2], 16) for i in (1, 3, 5))
    return "#%02x%02x%02x" % (int(r * f), int(g * f), int(b * f))


def texto_orcamento(cliente, peca, qtd, preco_un, total, dias):
    prazo = max(1, math.ceil(dias))
    saud = f"Olá, {cliente}! Tudo bem?" if cliente else "Olá! Tudo bem?"
    return (f"{saud}\n"
            f"Segue seu orçamento da *CTRL 3D – Impressão 3D*:\n\n"
            f"• Peça: *{peca}*\n"
            f"• Quantidade: *{qtd} un.*\n"
            f"• Valor unitário: *{brl(preco_un)}*\n"
            f"• Valor total: *{brl(total)}*\n"
            f"• Prazo de produção: aprox. *{prazo} dia{'s' if prazo > 1 else ''}* após a confirmação\n\n"
            f"Qualquer dúvida é só chamar!\n"
            f"_Modele • Imprima • Crie_")


# ----------------------------------------------------------------- leitura do arquivo do fatiador
def _horas_txt(s):
    """'1d 2h 3m 4s' -> horas"""
    tot = 0.0
    for n, u in re.findall(r"(\d+(?:\.\d+)?)\s*([dhms])", s.lower()):
        tot += float(n) * {"d": 86400, "h": 3600, "m": 60, "s": 1}[u]
    return tot / 3600


def _lista(s, sep=","):
    out = []
    for x in s.split(sep):
        try:
            out.append(float(x.strip().rstrip("m").strip() or 0))
        except ValueError:
            out.append(0.0)
    return out


def _caixa_de_pontos(pts):
    """[(x, y), ...] -> (largura, profundidade) em mm, ou None."""
    try:
        xs = [float(p[0]) for p in pts]
        ys = [float(p[1]) for p in pts]
    except (TypeError, ValueError, IndexError):
        return None
    if not xs:
        return None
    return (round(max(xs) - min(xs), 1), round(max(ys) - min(ys), 1))


def capacidade_mesa(mesa, peca, espaco=5.0, borda=0.0):
    """Quantas peças (largura x profundidade) cabem numa mesa, em grade, girando se couber mais.
    espaco = folga entre peças; borda = margem livre em volta da mesa."""
    W, H = mesa[0] - 2 * borda, mesa[1] - 2 * borda
    w, h = peca
    if w <= 0 or h <= 0 or W <= 0 or H <= 0:
        return 0

    def grade(a, b):
        return max(0, math.floor((W + espaco) / (a + espaco))) * max(0, math.floor((H + espaco) / (b + espaco)))
    return max(grade(w, h), grade(h, w))


def _ler_gcode_texto(txt):
    info = {"t": 0.0, "fil": [], "pecas": None, "mesa": None, "caixas": [], "altura": None}
    m = (re.search(r";\s*estimated printing time[^=\n]*=\s*([^\n]+)", txt)
         or re.search(r"total estimated time:\s*([^;\n]+)", txt)
         or re.search(r"model printing time:\s*([^;\n]+)", txt))
    if m:
        info["t"] = _horas_txt(m.group(1))
    else:
        m = re.search(r"^;\s*TIME:\s*(\d+)", txt, re.M)          # Cura / Anycubic Slicer antigo
        if m:
            info["t"] = int(m.group(1)) / 3600

    gs = []
    m = re.search(r";\s*filament used \[g\]\s*=\s*([^\n]+)", txt)
    if m:
        gs = _lista(m.group(1))
    else:
        m = re.search(r";\s*total filament (?:used|weight) \[g\]\s*[:=]\s*([\d.]+)", txt)
        if m:
            gs = [float(m.group(1))]
        else:
            m = re.search(r"^;\s*Filament used:\s*([^\n]+)", txt, re.M)   # Cura: metros
            if m:
                gs = [x * 2.98 for x in _lista(m.group(1))]                 # 1 m de 1,75 mm PLA ≈ 2,98 g
    cores = []
    for chave in ("filament_colour", "extruder_colour"):
        m = re.search(r";\s*" + chave + r"\s*=\s*([^\n]+)", txt)
        if m and m.group(1).strip().strip('"'):
            cores = [c.strip().strip('"') for c in m.group(1).split(";")]
            break
    m = re.search(r";\s*filament_type\s*=\s*([^\n]+)", txt)
    tipos = [c.strip() for c in m.group(1).split(";")] if m else []
    for i, g in enumerate(gs):
        info["fil"].append((g, cores[i] if i < len(cores) else "", tipos[i] if i < len(tipos) else ""))

    # altura da peça
    m = (re.search(r";\s*max_layer_z\s*=\s*([\d.]+)", txt) or re.search(r";\s*max_z_height\s*[:=]\s*([\d.]+)", txt)
         or re.search(r"^;\s*MAXZ:\s*([\d.]+)", txt, re.M))
    if m:
        info["altura"] = round(float(m.group(1)), 1)

    # tamanho da mesa da impressora
    m = re.search(r";\s*(?:printable_area|bed_shape)\s*=\s*([^\n]+)", txt)
    if m:
        info["mesa"] = _caixa_de_pontos(re.findall(r"(-?[\d.]+)\s*x\s*(-?[\d.]+)", m.group(1)))

    # contorno de cada peça (para saber quanto espaço ocupa na mesa)
    caixas = []
    m = re.search(r";\s*objects_info\s*=\s*(\{[^\n]+\})", txt)      # PrusaSlicer / .bgcode
    if m:
        try:
            objs = json.loads(m.group(1)).get("objects", [])
            info["pecas"] = len(objs) or None
            caixas = [_caixa_de_pontos(o.get("polygon") or []) for o in objs]
        except ValueError:
            pass
    if not caixas:                                                     # Klipper (Anycubic / Orca)
        for nome, poli in re.findall(r"EXCLUDE_OBJECT_DEFINE NAME=(\S+)[^\n]*?POLYGON=(\[\[[^\n]*?\]\])", txt):
            try:
                caixas.append(_caixa_de_pontos(json.loads(poli)))
            except ValueError:
                pass
    info["caixas"] = [c for c in caixas if c and c[0] > 0 and c[1] > 0]
    if info["pecas"]:
        return info
    ids = (set(re.findall(r"EXCLUDE_OBJECT_DEFINE NAME=(\S+)", txt))
           or set(re.findall(r"start printing object, unique label id:\s*(\d+)", txt))
           or set(re.findall(r"^M486 S(\d+)", txt, re.M))
           or {x for x in re.findall(r"^;MESH:(.+)$", txt, re.M) if x.strip() != "NONMESH"})
    if ids:
        info["pecas"] = len(ids)
    return info


_EXCLUI_TIPO = re.compile(r"skirt|wipe|prime|purge|custom|tower|flush|intro", re.I)
_MOV = re.compile(r"^G[0-3](?![0-9])")             # G0/G1/G2/G3 (também "G1X10" sem espaço)
_EIXO = re.compile(r"([XYZE])(-?\d*\.?\d+)")


def _caixa_por_movimentos(f):
    """Mede o espaço que a(s) peça(s) ocupam na mesa seguindo o bico (só onde ele extruda peça,
    sem saia, torre de limpeza ou linha de purga). Funciona com qualquer .gcode de texto."""
    x = y = z = 0.0
    zmax = 0.0
    e_abs, ult_e = False, 0.0
    tipo_ok = True          # sem marcação de tipo: conta tudo (menos o que o comentário da linha excluir)
    xmin = ymin = float("inf")
    xmax = ymax = float("-inf")
    for bruto in f:
        ln = bruto.decode("ascii", "ignore") if isinstance(bruto, bytes) else bruto
        if ln.startswith(";"):
            m = re.match(r";\s*(?:TYPE|FEATURE)\s*:\s*(.+)", ln)
            if m:
                tipo_ok = not _EXCLUI_TIPO.search(m.group(1))
            continue
        if ln.startswith("M82"):
            e_abs = True
        elif ln.startswith("M83"):
            e_abs = False
        elif ln.startswith("G92"):
            m = re.search(r"E(-?\d*\.?\d+)", ln)
            if m:
                ult_e = float(m.group(1))
        elif _MOV.match(ln):
            cmd, _, coment = ln.partition(";")
            novo = dict(_EIXO.findall(cmd))
            ok = (not _EXCLUI_TIPO.search(coment)) if coment.strip() else tipo_ok
            nx, ny = float(novo.get("X", x)), float(novo.get("Y", y))
            z = float(novo.get("Z", z))
            if "E" in novo:
                ev = float(novo["E"])
                extruda = (ev > ult_e) if e_abs else (ev > 0)
                if e_abs:
                    ult_e = ev
                if extruda and ok:
                    zmax = max(zmax, z)
                    for px, py in ((x, y), (nx, ny)):
                        xmin, xmax, ymin, ymax = min(xmin, px), max(xmax, px), min(ymin, py), max(ymax, py)
            x, y = nx, ny
    if xmax <= xmin or ymax <= ymin:
        return None
    return (round(xmax - xmin, 1), round(ymax - ymin, 1), round(zmax, 1))      # largura, profundidade, altura


def _ler_gcode_arquivo(f):
    """Lê só o começo e o fim (arquivos grandes)."""
    f.seek(0, 2)
    tam = f.tell()
    f.seek(0)
    bloco = 3 * 1024 * 1024
    if tam <= 2 * bloco:
        dados = f.read()
    else:
        dados = f.read(bloco)
        f.seek(tam - bloco)
        dados += b"\n" + f.read()
    txt = dados.decode("utf-8", "ignore")
    info = _ler_gcode_texto(txt)
    info["img"] = _imagem_gcode(txt[:bloco])     # a miniatura fica no começo do arquivo
    if not info["caixas"] and (info["pecas"] or 1) == 1:
        # o arquivo não diz o tamanho da peça: mede pelo caminho do bico
        f.seek(0)
        cx = _caixa_por_movimentos(f)
        if cx:
            info["caixas"] = [cx[:2]]
            info["medido"] = True
            info["altura"] = info.get("altura") or cx[2]
    return info


def _imagem_uri(dados, formato):
    """Bytes da miniatura -> data URI (PNG/JPG direto; QOI é convertido para PNG)."""
    import base64
    if formato == "qoi":
        try:
            from PIL import Image
            buf = io.BytesIO()
            Image.open(io.BytesIO(dados)).save(buf, "PNG")
            dados, formato = buf.getvalue(), "png"
        except Exception:
            return None
    mime = "jpeg" if formato in ("jpg", "jpeg") else "png"
    return f"data:image/{mime};base64," + base64.b64encode(dados).decode()


def _melhor_imagem(cands):
    """cands: [(largura*altura, prioridade, bytes, formato)] -> data URI da melhor."""
    for _, _, dados, fmt in sorted(cands, key=lambda c: (c[1], -c[0])):
        uri = _imagem_uri(dados, fmt)
        if uri:
            return uri
    return None


def _imagem_gcode(txt):
    import base64
    cands = []
    for fmt, w, h, corpo in re.findall(
            r";\s*thumbnail(?:_(\w+))? begin (\d+)[x ](\d+)[^\n]*\n(.*?);\s*thumbnail(?:_\w+)? end", txt, re.S):
        b64 = "".join(ln.lstrip(";").strip() for ln in corpo.splitlines())
        try:
            dados = base64.b64decode(b64)
        except ValueError:
            continue
        fmt = (fmt or "png").lower()
        cands.append((int(w) * int(h), 1 if fmt == "qoi" else 0, dados, fmt))
    return _melhor_imagem(cands)


def _imagem_3mf(z, nomes):
    """Imagem da mesa dentro do .3mf (plate_1.png do Anycubic/Orca/Bambu, ou thumbnail do Prusa)."""
    prefer = ["Metadata/plate_1.png", "Metadata/thumbnail.png", "Auxiliaries/.thumbnails/thumbnail_middle.png",
              "Metadata/plate_1_small.png"]
    pngs = [n for n in prefer if n in nomes] + sorted(
        n for n in nomes if n.lower().endswith((".png", ".jpg")) and ("plate" in n.lower() or "thumb" in n.lower())
        and "no_light" not in n.lower() and "top_" not in n.lower() and "pick_" not in n.lower())
    for n in pngs:
        try:
            dados = z.read(n)
        except KeyError:
            continue
        if len(dados) < 3 * 1024 * 1024:
            return _imagem_uri(dados, "jpg" if n.lower().endswith(".jpg") else "png")
    return None


def _geometria_3mf(z, nomes):
    """Tamanho da mesa (project_settings.config) e das peças (plate_1.json) dentro do .3mf."""
    geo = {"mesa": None, "caixas": []}
    if "Metadata/project_settings.config" in nomes:
        try:
            cfg = json.loads(z.read("Metadata/project_settings.config"))
            area = cfg.get("printable_area") or cfg.get("bed_shape")
            if isinstance(area, list):
                geo["mesa"] = _caixa_de_pontos([p.split("x") for p in area])
        except (ValueError, AttributeError):
            pass
    pj = next((n for n in ("Metadata/plate_1.json",) if n in nomes), None)
    if pj:
        try:
            for o in json.loads(z.read(pj)).get("bbox_objects", []):
                b = o.get("bbox") or []
                if len(b) == 4:
                    geo["caixas"].append((round(b[2] - b[0], 1), round(b[3] - b[1], 1)))
        except (ValueError, AttributeError, TypeError):
            pass
    return geo


def _heatshrink(dados, janela=12, adiante=4):
    """Descompacta Heatshrink (LZSS) — usado nos blocos de G-code do .bgcode."""
    bits = "".join(f"{b:08b}" for b in dados)
    n, pos = len(bits), 0
    out = bytearray()
    while pos < n:
        if bits[pos] == "1":                       # literal: 8 bits
            if pos + 9 > n:
                break
            out.append(int(bits[pos + 1:pos + 9], 2))
            pos += 9
        else:                                      # referência: posição + quantidade
            if pos + 1 + janela + adiante > n:
                break
            i = int(bits[pos + 1:pos + 1 + janela], 2) + 1
            c = int(bits[pos + 1 + janela:pos + 1 + janela + adiante], 2) + 1
            pos += 1 + janela + adiante
            ini = len(out) - i
            if ini < 0:
                break
            for k in range(c):
                out.append(out[ini + k])
    return bytes(out)


_MP_TAB = "0123456789. \nGX"


def _meatpack(dados):
    """Desempacota MeatPack (2 caracteres comuns por byte) — usado no G-code do .bgcode."""
    out = []
    ativo = sem_espaco = False
    i, n = 0, len(dados)
    while i < n:
        b = dados[i]
        if b == 0xFF and i + 2 < n and dados[i + 1] == 0xFF:      # comando
            cmd = dados[i + 2]
            i += 3
            if cmd == 251:
                ativo = True
            elif cmd == 250:
                ativo = False
            elif cmd == 247:
                sem_espaco = True
            elif cmd == 246:
                sem_espaco = False
            elif cmd == 249:
                ativo = sem_espaco = False
            continue
        i += 1
        if not ativo:
            out.append(chr(b))
            continue
        baixo, alto = b & 0xF, b >> 4
        ch = (lambda v: "E" if (v == 11 and sem_espaco) else _MP_TAB[v])
        if baixo == 0xF and alto == 0xF:
            out.append(chr(dados[i]) if i < n else "")
            out.append(chr(dados[i + 1]) if i + 1 < n else "")
            i += 2
        elif baixo == 0xF:
            out.append(chr(dados[i]) if i < n else "")
            i += 1
            out.append(ch(alto))
        elif alto == 0xF:
            out.append(ch(baixo))
            out.append(chr(dados[i]) if i < n else "")
            i += 1
        else:
            out.append(ch(baixo))
            out.append(ch(alto))
    return "".join(out)


def _bloco_gcode_texto(dados, comp, codif, tam):
    if comp in (2, 3):
        dados = _heatshrink(dados, 11 if comp == 2 else 12, 4)
    elif comp == 1:
        dados = zlib.decompress(dados)
    elif comp != 0:
        return ""
    return _meatpack(dados) if codif in (1, 2) else dados.decode("ascii", "ignore")


def _ler_bgcode(caminho):
    """G-code binário (.bgcode): lê só os blocos de informações (tempo, filamento, cores)."""
    with open(caminho, "rb") as f:
        d = f.read()
    if d[:4] != b"GCDE":
        raise ValueError("Esse .bgcode não parece válido.")
    checksum = struct.unpack_from("<H", d, 8)[0]
    pos, textos, imgs, blocos_gcode = 10, [], [], []
    while pos + 8 <= len(d):
        tipo, comp, tam = struct.unpack_from("<HHI", d, pos)
        pos += 8
        tam_disco = tam
        if comp:
            tam_disco = struct.unpack_from("<I", d, pos)[0]
            pos += 4
        params = d[pos:pos + (6 if tipo == 5 else 2)]
        pos += len(params)                          # parâmetros do bloco
        dados = d[pos:pos + tam_disco]
        pos += tam_disco + (4 if checksum == 1 else 0)
        if tipo == 1:                               # blocos de G-code: guarda para medir a peça
            blocos_gcode.append((dados, comp, struct.unpack("<H", params)[0] if len(params) == 2 else 0, tam))
            continue
        if tipo == 5 and comp == 0 and len(params) == 6:      # miniatura
            fmt, w, h = struct.unpack("<HHH", params)
            nome = {0: "png", 1: "jpg", 2: "qoi"}.get(fmt)
            if nome:
                imgs.append((w * h, 1 if nome == "qoi" else 0, dados, nome))
            continue
        if tipo in (0, 2, 3, 4) and comp in (0, 1):
            try:
                txt = (zlib.decompress(dados) if comp == 1 else dados).decode("utf-8", "ignore")
            except zlib.error:
                continue
            textos += ["; " + ln.replace("=", " = ", 1) for ln in txt.splitlines() if "=" in ln]
    if not textos:
        raise ValueError("Não encontrei as informações dentro desse .bgcode.")
    info = _ler_gcode_texto("\n".join(textos))
    info["img"] = _melhor_imagem(imgs)
    # tamanho da peça: o PrusaSlicer grava o contorno (objects_info) logo no começo do G-code
    if not info["caixas"] and blocos_gcode:
        try:
            inicio = _bloco_gcode_texto(*blocos_gcode[0])
            extra = _ler_gcode_texto(inicio)
            info["caixas"] = extra["caixas"]
            info["pecas"] = info["pecas"] or extra["pecas"]
            if not info["caixas"] and (info["pecas"] or 1) == 1 and len(blocos_gcode) <= 120:
                # sem contorno: descompacta tudo e mede pelo caminho do bico
                texto = "".join(_bloco_gcode_texto(*b) for b in blocos_gcode)
                cx = _caixa_por_movimentos(io.StringIO(texto))
                if cx:
                    info["caixas"], info["medido"] = [cx[:2]], True
                    info["altura"] = info.get("altura") or (cx[2] if len(cx) > 2 else None)
        except Exception:
            pass                                    # sem tamanho: a pessoa digita
    return info


def ler_fatiador(caminho):
    """Retorna {'t': horas, 'fil': [(gramas, cor, tipo)], 'pecas': n|None, 'placas': n}."""
    if caminho.lower().endswith(".bgcode"):
        info = _ler_bgcode(caminho)
        info["placas"] = 1
        if info["t"] <= 0 and not info["fil"]:
            raise ValueError("Não encontrei tempo nem filamento nesse arquivo.")
        return info
    if caminho.lower().endswith((".stl", ".obj", ".step", ".stp")):
        raise ValueError("Esse é o modelo 3D, ainda sem tempo de impressão.\nAbra no Anycubic Slicer, clique em "
                         "Fatiar e salve o arquivo fatiado (.gcode, .bgcode ou .gcode.3mf).")
    if caminho.lower().endswith(".3mf"):
        with zipfile.ZipFile(caminho) as z:
            nomes = z.namelist()
            if "Metadata/slice_info.config" in nomes:
                root = ET.fromstring(z.read("Metadata/slice_info.config"))
                t, fil, pecas, placas = 0.0, {}, 0, 0
                for pl in root.iter("plate"):
                    meta = {m.get("key"): m.get("value") for m in pl.findall("metadata")}
                    pred = num(meta.get("prediction"))
                    if pred <= 0:
                        continue
                    placas += 1
                    t += pred / 3600
                    pecas += sum(1 for o in pl.findall("object") if o.get("skipped", "false") != "true")
                    for fl in pl.findall("filament"):
                        k = fl.get("id")
                        g0 = fil.get(k, (0.0, fl.get("color", ""), fl.get("type", "")))
                        fil[k] = (g0[0] + num(fl.get("used_g")), g0[1], g0[2])
                if placas:
                    info = {"t": t, "fil": [fil[k] for k in sorted(fil, key=lambda x: int(num(x)))],
                            "pecas": pecas or None, "placas": placas, "img": _imagem_3mf(z, nomes)}
                    info.update(_geometria_3mf(z, nomes))
                    return info
            gcodes = sorted(n for n in nomes if n.lower().endswith(".gcode"))
            if gcodes:
                with z.open(gcodes[0]) as f:
                    info = _ler_gcode_arquivo(io.BytesIO(f.read()))
                info["placas"] = 1
                info["img"] = info.get("img") or _imagem_3mf(z, nomes)
                geo = _geometria_3mf(z, nomes)
                info["mesa"] = info.get("mesa") or geo.get("mesa")
                info["caixas"] = info.get("caixas") or geo.get("caixas", [])
                return info
        raise ValueError("Esse .3mf ainda não foi fatiado.\nNo Anycubic Slicer clique em Fatiar e depois "
                         "exporte/salve o arquivo fatiado (.gcode ou .gcode.3mf).")
    with open(caminho, "rb") as f:
        info = _ler_gcode_arquivo(f)
    info["placas"] = 1
    if info["t"] <= 0 and not info["fil"]:
        raise ValueError("Não encontrei tempo nem filamento nesse arquivo.\nUse o .gcode gerado pelo fatiador.")
    return info


# ----------------------------------------------------------------- motor de cálculo
def rolos_db(db):
    rol = {None: ("Filamento padrão", db.cfg("preco_kg_padrao") / 1000, None)}
    for r in db.all("SELECT * FROM filamentos"):
        rol[r["id"]] = (f"{r['nome']} {r['tipo']}", (r["preco"] / r["peso_g"]) if r["peso_g"] else 0,
                        r["restante_g"])
    return rol


def fila_atual(db):
    r = db.one("SELECT COUNT(*), COALESCE(SUM(horas),0) FROM pedidos WHERE status IN ('Aprovado','Em produção')")
    return r[0], r[1]


def calcular_pedido(db, e, rolos, canal=(0, 0), fila_h=0.0):
    """e: qtd (peças do pedido), ppm (peças NO ARQUIVO), t_mesa (tempo do arquivo, h),
    cores [(id_rolo, g do arquivo)], por_mesa (peças por impressão na produção; 0 = automático),
    peca_l/peca_p (mm), purga (g por impressão), preparo, acab, emb, extras, margem, preco_manual.

    O arquivo diz quanto custa 1 peça (tempo e gramas ÷ peças do arquivo). O pedido multiplica isso pela
    quantidade e divide em impressões conforme o que cabe na mesa."""
    qtd = max(1, int(e.get("qtd", 1)))
    ppm = max(1, int(e.get("ppm", 1)))
    t_mesa = max(0.0, e.get("t_mesa", 0))
    t_peca = t_mesa / ppm

    # quantas cabem na mesa (pelo tamanho da peça)
    mesa_mm = (db.cfg("mesa_x"), db.cfg("mesa_y"))
    peca_mm = (max(0.0, e.get("peca_l", 0)), max(0.0, e.get("peca_p", 0)))
    tem_medida = peca_mm[0] > 0 and peca_mm[1] > 0
    cap = capacidade_mesa(mesa_mm, peca_mm, db.cfg("espaco_mm"), db.cfg("borda_mm")) if tem_medida else 0
    pedido_mesa = max(0, int(e.get("por_mesa", 0)))
    por_mesa = pedido_mesa or cap or ppm          # automático: o máximo que cabe (ou como está no arquivo)
    if cap:
        por_mesa = min(por_mesa, cap)
    por_mesa = max(1, min(por_mesa, qtd))         # nunca mais do que o pedido
    mesas = math.ceil(qtd / por_mesa)
    sobra = mesas * por_mesa - qtd

    aquece = db.cfg("aquecer_min") / 60           # aquecer e nivelar a cada impressão
    h_maq = t_peca * qtd + aquece * mesas
    troca = db.cfg("troca_min")
    h_troca = max(0, mesas - 1) * troca / 60
    h_total = h_maq + h_troca
    hdia = max(1.0, db.cfg("horas_dia"))
    dias = h_total / hdia
    dias_fila = (fila_h + h_total) / hdia

    usados, g_pecas = [], 0.0
    for rid, gmesa in e.get("cores", []):
        gr = max(0.0, gmesa) / ppm * qtd
        if gr <= 0:
            continue
        if rid not in rolos:
            rid = None
        nome, pg, rest = rolos[rid]
        usados.append((rid, pg, nome, rest, gr))
        g_pecas += gr
    g_purga = max(0.0, e.get("purga", 0)) * mesas
    consumo, nomes, rest_rolo, c_fil = {}, {}, {}, 0.0
    for rid, pg, nome, rest, gr in usados:
        tot = gr + (g_purga * gr / g_pecas if g_pecas else 0)
        k = rid if rid is not None else "padrao"
        consumo[k] = consumo.get(k, 0) + tot
        nomes[k], rest_rolo[k] = nome, rest
        c_fil += tot * pg
    if not usados and g_purga:
        c_fil += g_purga * rolos[None][1]

    kwh = h_maq * db.cfg("watts") / 1000
    c_ene = kwh * db.cfg("kwh_preco")
    c_maq = h_maq * (db.cfg("maquina_valor") / max(1.0, db.cfg("vida_horas")) + db.cfg("manut_hora"))
    c_falha = (c_fil + c_ene + c_maq) * db.cfg("falha_pct") / 100
    min_trab = e.get("preparo", 0) + e.get("acab", 0) * qtd + max(0, mesas - 1) * troca
    c_mo = min_trab / 60 * db.cfg("hora_trabalho")
    c_emb = (e.get("emb", 0) + e.get("extras", 0)) * qtd
    custo = c_fil + c_ene + c_maq + c_falha + c_mo + c_emb

    pct, fixa = canal
    imp = db.cfg("imposto_pct")
    den = max(0.05, 1 - (pct + imp) / 100)
    sug_total = (custo * (1 + e.get("margem", 0) / 100) + fixa) / den
    sug_un = arredonda_preco(sug_total / qtd)
    manual = e.get("preco_manual", 0)
    preco_un = manual if manual > 0 else sug_un
    total = preco_un * qtd
    taxas = total * pct / 100 + (fixa if total else 0)
    imposto = total * imp / 100
    lucro = total - taxas - imposto - custo

    # aproveitamento da mesa: se você escolheu menos peças por impressão do que cabe, mostra a economia
    mesas_cap = math.ceil(qtd / min(cap, qtd)) if cap else mesas
    menos = max(0, mesas - mesas_cap)
    aproveitamento = dict(
        cap=cap, mesa=mesa_mm, peca=peca_mm, por_mesa=por_mesa, auto=not pedido_mesa,
        uso=min(100, round(100 * por_mesa / cap)) if cap else 0,
        menos=menos, mesas_cap=mesas_cap,
        troca_h=menos * troca / 60 + menos * aquece,        # trocas e aquecimentos que deixa de fazer
        purga_g=menos * max(0.0, e.get("purga", 0)),        # purga que deixa de gastar
        nao_cabe=bool(tem_medida and cap == 0))

    alertas = []
    altura = max(0.0, e.get("peca_a", 0))
    aproveitamento["altura"] = altura
    if altura > db.cfg("mesa_z") > 0:
        alertas.append(f"⚠ A peça tem {fnum(altura, 0)} mm de altura e a impressora vai até "
                       f"{fnum(db.cfg('mesa_z'), 0)} mm.")
    if aproveitamento["nao_cabe"]:
        alertas.append(f"⚠ A peça ({fnum(peca_mm[0], 0)} × {fnum(peca_mm[1], 0)} mm) é maior que a mesa "
                       f"({fnum(mesa_mm[0], 0)} × {fnum(mesa_mm[1], 0)} mm).")
    elif cap and pedido_mesa > cap:
        alertas.append(f"⚠ Pela medida da peça cabem no máximo {cap} por impressão — usei {cap}.")
    elif not tem_medida and not pedido_mesa and qtd > ppm:
        alertas.append(f"⚠ Sem o tamanho da peça, contei {ppm} por impressão ({mesas} impressões). "
                       f"Coloque o tamanho (ou quantas por impressão) para o tempo ficar certo.")
    for k, gr in consumo.items():
        if k != "padrao" and rest_rolo[k] is not None and gr > rest_rolo[k]:
            alertas.append(f"⚠ {nomes[k]}: precisa de {fpeso(gr)}, tem só {fpeso(rest_rolo[k])} no estoque")
    if manual > 0 and lucro < 0:
        alertas.append("⚠ Com esse preço você tem PREJUÍZO.")

    return dict(qtd=qtd, ppm=ppm, mesas=mesas, h_maq=h_maq, h_troca=h_troca, h_total=h_total, hdia=hdia,
                dias=dias, dias_fila=dias_fila, fila_h=fila_h,
                pronto=hoje() + dt.timedelta(days=max(1, math.ceil(dias_fila))), g_pecas=g_pecas,
                g_purga=g_purga, g_total=g_pecas + g_purga, consumo=consumo, nomes=nomes, kwh=kwh,
                c_fil=c_fil, c_ene=c_ene, c_maq=c_maq, c_falha=c_falha, c_mo=c_mo, c_emb=c_emb, custo=custo,
                sug_un=sug_un, preco_un=preco_un, total=total, taxas=taxas, imposto=imposto, lucro=lucro,
                pct=pct, fixa=fixa, alertas=alertas, min_trab=min_trab, t_mesa=t_mesa, sobra=sobra,
                mesa=aproveitamento, t_peca=t_peca, por_mesa=por_mesa, h_aquece=aquece * mesas)


CORES_NOMES = {  # cor de referência -> palavras que podem aparecer no nome do rolo
    (255, 255, 255): ("branco", "white"), (0, 0, 0): ("preto", "black"), (128, 128, 128): ("cinza", "grey", "gray"),
    (192, 192, 192): ("prata", "silver", "cinza"), (220, 20, 30): ("vermelho", "red"),
    (236, 0, 140): ("magenta", "pink", "rosa"), (255, 150, 200): ("rosa", "pink"),
    (0, 174, 239): ("ciano", "azul", "cyan", "blue"), (20, 60, 200): ("azul", "blue"),
    (255, 210, 0): ("amarelo", "yellow"), (255, 130, 0): ("laranja", "orange"), (0, 160, 70): ("verde", "green"),
    (130, 50, 170): ("roxo", "lilas", "purple"), (120, 70, 30): ("marrom", "brown"), (212, 175, 55): ("dourado", "gold"),
    (240, 230, 200): ("bege", "marfim", "beige"),
}


def sem_acento(s):
    import unicodedata
    return "".join(c for c in unicodedata.normalize("NFD", (s or "").lower()) if unicodedata.category(c) != "Mn")


def nome_da_cor(hexc):
    try:
        r, g, b = (int(hexc[i:i + 2], 16) for i in (1, 3, 5))
    except (ValueError, TypeError, IndexError):
        return ()
    ref = min(CORES_NOMES, key=lambda c: (c[0] - r) ** 2 + (c[1] - g) ** 2 + (c[2] - b) ** 2)
    return CORES_NOMES[ref]


def _rgb(hexc):
    try:
        return tuple(int(hexc[i:i + 2], 16) for i in (1, 3, 5))
    except (ValueError, TypeError, IndexError):
        return None


def rolo_parecido(db, tipo, hexcor):
    """Rolo do estoque mais parecido com a cor do arquivo: pela cor cadastrada, senão pelo nome da cor."""
    alvo = _rgb(hexcor)
    if not alvo:
        return None
    tipo = (tipo or "").upper()
    rolos = db.all("SELECT id, nome, tipo, cor FROM filamentos WHERE restante_g>0 ORDER BY nome")
    melhores = []
    for r in rolos:
        c = _rgb(r["cor"] or "")
        if c:
            dist = sum((a - b) ** 2 for a, b in zip(c, alvo))
            if dist < 60 ** 2 * 3:
                melhores.append((dist + (0 if (r["tipo"] or "").upper() == tipo else 1), r["id"]))
    if melhores:
        return min(melhores)[1]
    nomes = nome_da_cor(hexcor)
    achados = [r for r in rolos if any(n in sem_acento(r["nome"]) for n in nomes)]
    achados.sort(key=lambda r: (r["tipo"] or "").upper() != tipo)
    return achados[0]["id"] if achados else None


def previsoes_fila(db):
    """{id_pedido: data_iso} para os pedidos na fila, e o total de horas da fila."""
    hdia = max(1.0, db.cfg("horas_dia"))
    acum, prev = 0.0, {}
    for r in db.all("SELECT id, horas FROM pedidos WHERE status IN ('Aprovado','Em produção') "
                    "ORDER BY CASE status WHEN 'Em produção' THEN 0 ELSE 1 END, id"):
        acum += r["horas"]
        prev[r["id"]] = (hoje() + dt.timedelta(days=max(1, math.ceil(acum / hdia)))).isoformat()
    for pid, d in prev.items():
        db.c.execute("UPDATE pedidos SET entrega=? WHERE id=?", (d, pid))
    db.c.commit()
    return prev, acum, hdia


def meses_disponiveis(db):
    ms = {hoje().isoformat()[:7]}
    for t in ("pedidos", "gastos", "pessoal"):
        ms |= {r[0] for r in db.all(f"SELECT DISTINCT substr(data,1,7) FROM {t}") if r[0]}
    return sorted(ms, reverse=True)


def dados_resumo(db, ym):
    validos = "status NOT IN ('Orçamento','Cancelado')"
    p = db.one(f"SELECT COUNT(*) n, COALESCE(SUM(preco),0) v, COALESCE(SUM(lucro),0) l, COALESCE(SUM(horas),0) h,"
               f" COALESCE(SUM(gramas),0) g, COALESCE(SUM(custo),0) c, COALESCE(SUM(qtd),0) q FROM pedidos"
               f" WHERE substr(data,1,7)=? AND {validos}", (ym,))
    rec = db.one("SELECT COALESCE(SUM(preco),0) FROM pedidos WHERE substr(data,1,7)=? AND status='Pago'", (ym,))[0]
    orc = db.one("SELECT COUNT(*) FROM pedidos WHERE substr(data,1,7)=? AND status='Orçamento'", (ym,))[0]
    gas = db.one("SELECT COALESCE(SUM(valor),0) FROM gastos WHERE substr(data,1,7)=?", (ym,))[0]
    cats = db.all("SELECT categoria, SUM(valor) v FROM gastos WHERE substr(data,1,7)=? GROUP BY categoria "
                  "ORDER BY v DESC", (ym,))
    top = db.all(f"SELECT peca, SUM(qtd) q, SUM(preco) v FROM pedidos WHERE substr(data,1,7)=? AND {validos} "
                 f"GROUP BY lower(peca) ORDER BY q DESC LIMIT 6", (ym,))
    return p, rec, orc, gas, cats, top


def dados_pessoal(db, ym):
    tot = db.one("SELECT COUNT(*) n, COALESCE(SUM(gasto),0) g, COALESCE(SUM(gramas),0) gr, COALESCE(SUM(horas),0) h "
                 "FROM pessoal WHERE substr(data,1,7)=?", (ym,))
    itens = db.all("SELECT * FROM pessoal WHERE substr(data,1,7)=? ORDER BY id DESC", (ym,))
    return tot, itens


def exportar_excel(db, ym, arq):
    from openpyxl import Workbook
    from openpyxl.styles import Font, PatternFill, Alignment
    p, rec, orc, gas, cats, top = dados_resumo(db, ym)
    wb = Workbook()
    cab_font, cab_fill = Font(bold=True, color="FFFFFF"), PatternFill("solid", fgColor="111111")
    moeda = 'R$ #,##0.00'

    def cabecalho(ws, cols, larg):
        ws.append(cols)
        for i, c in enumerate(ws[ws.max_row], 1):
            c.font, c.fill, c.alignment = cab_font, cab_fill, Alignment(horizontal="center")
            ws.column_dimensions[c.column_letter].width = larg[i - 1]

    ws = wb.active
    ws.title = "Resumo"
    ws.append([f"CTRL 3D – Resumo {mes_br(ym)}"])
    ws["A1"].font = Font(bold=True, size=14)
    ws.append([])
    cabecalho(ws, ["Item", "Valor"], [32, 18])
    for nome, v, fmt in [("Vendido", p["v"], moeda), ("Recebido (pago)", rec, moeda), ("Gastos", gas, moeda),
                         ("Saldo de caixa", rec - gas, moeda), ("Custo dos pedidos", p["c"], moeda),
                         ("Lucro dos pedidos", p["l"], moeda), ("Horas de impressão", round(p["h"], 2), "0.00"),
                         ("Filamento usado (kg)", round(p["g"] / 1000, 3), "0.000"), ("Pedidos", p["n"], "0"),
                         ("Orçamentos em aberto", orc, "0")]:
        ws.append([nome, v])
        ws.cell(ws.max_row, 2).number_format = fmt
    ws = wb.create_sheet("Pedidos")
    cabecalho(ws, ["#", "Data", "Cliente", "Peça", "Qtd", "Canal", "Horas", "Filamento (g)", "Custo", "Valor",
                   "Lucro", "Status", "Previsão"], [6, 12, 22, 26, 7, 26, 9, 13, 13, 13, 13, 14, 12])
    for r in db.all("SELECT * FROM pedidos WHERE substr(data,1,7)=? ORDER BY id", (ym,)):
        ws.append([r["id"], data_br(r["data"]), r["cliente"], r["peca"], r["qtd"], r["canal"], round(r["horas"], 2),
                   round(r["gramas"], 1), r["custo"], r["preco"], r["lucro"], r["status"], data_br(r["entrega"])])
        for col in (9, 10, 11):
            ws.cell(ws.max_row, col).number_format = moeda
    ws = wb.create_sheet("Gastos")
    cabecalho(ws, ["Data", "Categoria", "Descrição", "Valor"], [12, 22, 40, 14])
    for r in db.all("SELECT * FROM gastos WHERE substr(data,1,7)=? ORDER BY data", (ym,)):
        ws.append([data_br(r["data"]), r["categoria"], r["descricao"], r["valor"]])
        ws.cell(ws.max_row, 4).number_format = moeda
    ws = wb.create_sheet("Estoque")
    cabecalho(ws, ["Nome", "Marca", "Tipo", "R$/kg", "Restante (g)"], [24, 18, 12, 12, 14])
    for r in db.all("SELECT * FROM filamentos ORDER BY nome"):
        ws.append([r["nome"], r["marca"], r["tipo"], r["preco"] / r["peso_g"] * 1000 if r["peso_g"] else 0,
                   round(r["restante_g"])])
        ws.cell(ws.max_row, 4).number_format = moeda
    wb.save(arq)


def contraste(hexc):
    try:
        r, g, b = (int(hexc[i:i + 2], 16) for i in (1, 3, 5))
        return BK if (0.299 * r + 0.587 * g + 0.114 * b) > 160 else "white"
    except (ValueError, TypeError):
        return "white"


# ----------------------------------------------------------------- banco de dados
class Banco:
    def __init__(self, caminho):
        os.makedirs(os.path.dirname(caminho) or ".", exist_ok=True)
        self.caminho = caminho
        self.c = sqlite3.connect(caminho, check_same_thread=False)
        self.c.row_factory = sqlite3.Row
        self.c.executescript("""
        CREATE TABLE IF NOT EXISTS config(chave TEXT PRIMARY KEY, valor TEXT);
        CREATE TABLE IF NOT EXISTS filamentos(id INTEGER PRIMARY KEY AUTOINCREMENT, nome TEXT, marca TEXT,
            tipo TEXT, preco REAL, peso_g REAL, restante_g REAL);
        CREATE TABLE IF NOT EXISTS canais(id INTEGER PRIMARY KEY AUTOINCREMENT, nome TEXT, taxa_pct REAL, taxa_fixa REAL);
        CREATE TABLE IF NOT EXISTS pedidos(id INTEGER PRIMARY KEY AUTOINCREMENT, data TEXT, cliente TEXT, peca TEXT,
            qtd INTEGER, canal TEXT, horas REAL, gramas REAL, custo REAL, preco REAL, lucro REAL, status TEXT,
            entrega TEXT, detalhes TEXT, baixado INTEGER DEFAULT 0);
        CREATE TABLE IF NOT EXISTS produtos(id INTEGER PRIMARY KEY AUTOINCREMENT, nome TEXT UNIQUE, dados TEXT);
        CREATE TABLE IF NOT EXISTS pessoal(id INTEGER PRIMARY KEY AUTOINCREMENT, data TEXT, peca TEXT, qtd INTEGER,
            horas REAL, gramas REAL, gasto REAL, detalhes TEXT);
        CREATE TABLE IF NOT EXISTS gastos(id INTEGER PRIMARY KEY AUTOINCREMENT, data TEXT, categoria TEXT,
            descricao TEXT, valor REAL);
        """)
        colunas = {r[1] for r in self.c.execute("PRAGMA table_info(filamentos)")}
        if "cor" not in colunas:                       # versão nova: cor do rolo
            self.c.execute("ALTER TABLE filamentos ADD COLUMN cor TEXT DEFAULT ''")
        for k, _, v, _, _ in CFG:
            self.c.execute("INSERT OR IGNORE INTO config VALUES(?,?)", (k, str(v)))
        if not self.c.execute("SELECT 1 FROM canais").fetchone():
            self.c.executemany("INSERT INTO canais(nome,taxa_pct,taxa_fixa) VALUES(?,?,?)", [
                ("Balcão / WhatsApp (Pix ou dinheiro)", 0, 0),
                ("Cartão (maquininha)", 4.99, 0),
                ("Shopee", 20, 4),
                ("Mercado Livre", 14, 6.75),
            ])
        self.c.commit()

    def run(self, sql, a=()):
        cur = self.c.execute(sql, a)
        self.c.commit()
        return cur

    def all(self, sql, a=()):
        return self.c.execute(sql, a).fetchall()

    def one(self, sql, a=()):
        return self.c.execute(sql, a).fetchone()

    def cfg(self, k):
        r = self.one("SELECT valor FROM config WHERE chave=?", (k,))
        return num(r[0]) if r else 0.0

    def set_cfg(self, k, v):
        self.run("INSERT OR REPLACE INTO config VALUES(?,?)", (k, str(v)))

    def mexer_estoque(self, consumo, sinal):
        """consumo: {id_rolo: gramas}; sinal -1 desconta, +1 devolve."""
        for sid, g in consumo.items():
            self.c.execute("UPDATE filamentos SET restante_g = MAX(0, restante_g + ?) WHERE id=?",
                           (sinal * g, int(sid)))
        self.c.commit()


