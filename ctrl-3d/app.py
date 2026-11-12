# -*- coding: utf-8 -*-
"""CTRL 3D • Custos, prazos e pedidos — janela do programa (interface em HTML, lógica em core.py)."""
import os
import sys
import json
import math
import time
import shutil
import threading
import functools
import datetime as dt

import webview
from webview.dom import _dnd_state

import core as C
from core import num, brl, fnum, fhoras, fpeso, data_br, hoje


def recurso(rel):
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, rel)


def copiar_texto(txt):
    """Copia para a área de transferência do Windows (com acentos e emojis)."""
    import ctypes
    from ctypes import wintypes
    u32 = ctypes.WinDLL("user32", use_last_error=True)
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    u32.OpenClipboard.argtypes = [wintypes.HWND]
    u32.SetClipboardData.argtypes = [wintypes.UINT, wintypes.HANDLE]
    u32.SetClipboardData.restype = wintypes.HANDLE
    k32.GlobalAlloc.argtypes = [wintypes.UINT, ctypes.c_size_t]
    k32.GlobalAlloc.restype = wintypes.HGLOBAL
    k32.GlobalLock.argtypes = [wintypes.HGLOBAL]
    k32.GlobalLock.restype = ctypes.c_void_p
    k32.GlobalUnlock.argtypes = [wintypes.HGLOBAL]
    dados = txt.encode("utf-16-le") + b"\x00\x00"
    for _ in range(20):
        if u32.OpenClipboard(None):
            break
        time.sleep(0.05)
    else:
        raise RuntimeError("Área de transferência ocupada, tente de novo.")
    try:
        u32.EmptyClipboard()
        h = k32.GlobalAlloc(0x0002, len(dados))
        p = k32.GlobalLock(h)
        ctypes.memmove(p, dados, len(dados))
        k32.GlobalUnlock(h)
        u32.SetClipboardData(13, h)
    finally:
        u32.CloseClipboard()


def _dialogo_caminho(res):
    if not res:
        return None
    return res if isinstance(res, str) else res[0]


class Api:
    def __init__(self):
        self.db = C.Banco(C.caminho_db())
        self._janela = None

    # ------------------------------------------------------------------ geral
    def _rolos(self):
        aviso = self.db.cfg("aviso_g")
        out = []
        for r in self.db.all("SELECT * FROM filamentos ORDER BY nome"):
            pg = r["preco"] / r["peso_g"] if r["peso_g"] else 0
            out.append(dict(id=r["id"], nome=r["nome"], marca=r["marca"] or "", tipo=r["tipo"] or "",
                            cor=r["cor"] or "", rkg=brl(pg * 1000), restante=fpeso(r["restante_g"]),
                            pct=min(100, round(100 * r["restante_g"] / r["peso_g"])) if r["peso_g"] else 0,
                            baixo=r["restante_g"] < aviso, acabou=r["restante_g"] <= 0))
        return out

    def estado(self):
        db = self.db
        n, h = C.fila_atual(db)
        return dict(
            cfg={k: db.cfg(k) for k, *_ in C.CFG},
            cfg_def=[dict(k=k, rot=rot, uni=uni, dica=dica) for k, rot, _, uni, dica in C.CFG],
            canais=[dict(id=r["id"], nome=r["nome"], pct=r["taxa_pct"], fixa=r["taxa_fixa"],
                         txt=f"{fnum(r['taxa_pct'], 2)}%" + (f" + {brl(r['taxa_fixa'])}" if r["taxa_fixa"] else ""))
                    for r in db.all("SELECT * FROM canais ORDER BY id")],
            rolos=self._rolos(), padrao_kg=fnum(db.cfg("preco_kg_padrao"), 0),
            produtos=[r["nome"] for r in db.all("SELECT nome FROM produtos ORDER BY nome")],
            fila=dict(n=n, h=fhoras(h)), tipos=C.TIPOS, categorias=C.CATEGORIAS, status=C.STATUS,
            hoje=hoje().strftime("%d/%m/%Y"),
            meses=[dict(v=m, txt=C.mes_br(m)) for m in C.meses_disponiveis(db)],
            caminho=db.caminho)

    # ------------------------------------------------------------------ calculadora
    def _entrada(self, f):
        g = lambda k: max(0.0, num(f.get(k)))
        return dict(qtd=int(num(f.get("qtd"), 1)), ppm=int(num(f.get("ppm"), 1)), t_mesa=g("th") + g("tm") / 60,
                    cores=[(c.get("rolo"), num(c.get("g"))) for c in f.get("cores", [])],
                    purga=g("purga"), preparo=g("preparo"), acab=g("acab"), emb=g("emb"), extras=g("extras"),
                    margem=g("margem"), preco_manual=g("preco_manual"), peca_l=g("peca_l"), peca_p=g("peca_p"),
                    peca_a=g("peca_a"),
                    por_mesa=int(g("por_mesa")))

    def _canal(self, nome):
        r = self.db.one("SELECT taxa_pct, taxa_fixa FROM canais WHERE nome=?", (nome or "",))
        return (r[0], r[1]) if r else (0, 0)

    def _calc(self, f):
        n, h = C.fila_atual(self.db)
        return C.calcular_pedido(self.db, self._entrada(f), C.rolos_db(self.db), self._canal(f.get("canal")), h), n

    def calcular(self, f):
        r, nfila = self._calc(f)
        q, m, pm = r["qtd"], r["mesas"], r["por_mesa"]
        a = r["mesa"]
        pc = lambda k: f"{k} peça" + ("s" if k > 1 else "")
        imp = lambda k: f"{k} impress{'ões' if k > 1 else 'ão'}"
        if r["t_mesa"] <= 0:
            frase = ["Solte o arquivo do fatiador ou coloque o tempo de impressão."]
        else:
            frase = [f"<b>{pc(q)}</b> × {fhoras(r['t_peca'])} cada = <b>{fhoras(r['h_total'])}</b> no total"]
            if m > 1:
                frase.append(f"Em {imp(m)} de " + (f"até {pm} peças" if pm > 1 else "1 peça")
                             + (f" (é o que cabe na mesa)" if a["cap"] and pm == a["cap"] else ""))
            if r["fila_h"] > 0:
                frase.append(f"Antes dele tem {fhoras(r['fila_h'])} de pedidos na fila → fica pronto "
                             f"<b>{r['pronto'].strftime('%d/%m')}</b>")
            else:
                frase.append(f"Impressora livre → fica pronto <b>{r['pronto'].strftime('%d/%m')}</b>")
            if r["sobra"] and m > 1:
                frase.append(f"Dica: na última impressão ainda cabem mais {pc(r['sobra'])}.")
        txt_taxa = f"{fnum(r['pct'], 1)}%" + (f" + {brl(r['fixa'])}" if r["fixa"] else "")
        mesa_txt = f"{fnum(a['mesa'][0], 0)} × {fnum(a['mesa'][1], 0)} mm"
        mm = lambda v: fnum(v, 1).replace(",0", "")
        tamanho = (f"{mm(a['peca'][0])} × {mm(a['peca'][1])}" + (f" × {mm(a['altura'])}" if a["altura"] else "") + " mm"
                   if a["peca"][0] and a["peca"][1] else "")
        if a["cap"]:
            mesa_lin = [["Tamanho da peça (L × P × A)", tamanho],
                        ["Na mesa (" + mesa_txt + ") cabem", f"até {a['cap']} peças", 1],
                        ["Já descontando", f"{fnum(self.db.cfg('espaco_mm'), 0)} mm entre peças e "
                                           f"{fnum(self.db.cfg('borda_mm'), 0)} mm na borda"],
                        ["Por impressão neste pedido", f"{pm} ({a['uso']}% da mesa)" + (" • automático" if a["auto"] else "")]]
        elif a["nao_cabe"]:
            mesa_lin = [["Tamanho da peça (L × P × A)", tamanho],
                        ["Mesa", f"{mesa_txt} — não cabe!", 1]]
        else:
            mesa_lin = [["Tamanho da peça", "não informado — coloque para saber quantas cabem"],
                        ["Por impressão neste pedido", f"{pm}"]]
        dica = None
        if a["menos"] > 0:                       # você escolheu menos peças por impressão do que cabe
            ganhos = [f"{a['menos']} impress{'ões' if a['menos'] > 1 else 'ão'} a menos"]
            if a["troca_h"]:
                ganhos.append(f"{fhoras(a['troca_h'])} a menos trocando mesa e aquecendo")
            if a["purga_g"]:
                ganhos.append(f"{fpeso(a['purga_g'])} a menos de purga")
            dica = dict(cap=a["cap"], titulo=f"Cabem {a['cap']} por impressão (você está usando {pm})",
                        texto=f"{imp(a['mesas_cap'])} em vez de {m}: " + ", ".join(ganhos) + ".")
        # uso pessoal: só o que sai do bolso (filamento, luz e desgaste da máquina)
        gasto = r["c_fil"] + r["c_ene"] + r["c_maq"]
        pessoal = dict(
            total=brl(gasto), un=brl(gasto / q), filamento=fpeso(r["g_total"]),
            linhas=[["Filamento", f"{brl(r['c_fil'])} ({fpeso(r['g_total'])})"],
                    ["Energia elétrica", f"{brl(r['c_ene'])} ({fnum(r['kwh'], 2)} kWh)"],
                    ["Desgaste + manutenção da máquina", brl(r["c_maq"])],
                    ["Gasto total", brl(gasto), 1], ["Por peça", brl(gasto / q)]])
        return dict(
            mesa=mesa_lin, dica_mesa=dica, tamanho=tamanho, pessoal=pessoal,
            hero=dict(tempo=fhoras(r["h_total"]), pronto=r["pronto"].strftime("%d/%m"),
                      custo_un=brl(r["custo"] / q), custo=brl(r["custo"]), preco_un=brl(r["preco_un"]),
                      total=brl(r["total"])),
            frase=frase, alertas=r["alertas"],
            prazo=[["Tempo de 1 peça", fhoras(r["t_peca"])],
                   ["Impressões", f"{m} × até {pm} peça(s)"],
                   ["Imprimindo", fhoras(r["h_maq"]) + (f" (inclui {fhoras(r['h_aquece'])} aquecendo)" if r["h_aquece"] else "")],
                   ["Trocando a mesa", fhoras(r["h_troca"])],
                   ["Tempo total do pedido", fhoras(r["h_total"]), 1],
                   ["Só este pedido leva", f"{fnum(r['dias'], 1)} dias (a {fnum(r['hdia'], 0)}h/dia)"],
                   ["Pedidos na fila antes deste", f"{nfila} • {fhoras(r['fila_h'])}"],
                   ["Se fechar hoje, fica pronto", r["pronto"].strftime("%d/%m/%Y"), 1]],
            material=[["Filamento por peça", fpeso(r["g_pecas"] / q)], ["Filamento nas peças", fpeso(r["g_pecas"])],
                      ["Purga / desperdício", fpeso(r["g_purga"])], ["Filamento total", fpeso(r["g_total"]), 1]],
            rolos=[f"{r['nomes'][k]}: {fpeso(gr)}" for k, gr in r["consumo"].items()],
            custos=[["Filamento", brl(r["c_fil"])], ["Energia elétrica", f"{brl(r['c_ene'])} ({fnum(r['kwh'], 2)} kWh)"],
                    ["Desgaste + manutenção", brl(r["c_maq"])], ["Reserva para falhas", brl(r["c_falha"])],
                    ["Mão de obra", f"{brl(r['c_mo'])} ({fhoras(r['min_trab'] / 60)})"],
                    ["Embalagem + extras", brl(r["c_emb"])], ["Custo total", brl(r["custo"]), 1]],
            venda=[["Preço sugerido por unidade", brl(r["sug_un"])], ["Preço cobrado por unidade", brl(r["preco_un"])],
                   ["Valor total do pedido", brl(r["total"]), 1], ["Taxas do canal", f"{brl(r['taxas'])} ({txt_taxa})"],
                   ["Imposto", brl(r["imposto"])], ["Lucro líquido", brl(r["lucro"]), 1, "lucro" if r["lucro"] >= 0 else "prejuizo"],
                   ["Lucro por unidade", brl(r["lucro"] / q)],
                   ["Lucro por hora de máquina", brl(r["lucro"] / r["h_maq"]) if r["h_maq"] else "-"]])

    def importar(self, caminho):
        try:
            info = C.ler_fatiador(caminho)
        except (ValueError, OSError, C.zipfile.BadZipFile) as e:
            return dict(erro=str(e) or "Não consegui ler esse arquivo.")
        fil = [x for x in info["fil"] if x[0] > 0.05]
        tmin = round(info["t"] * 60)
        nome = os.path.basename(caminho)
        for ext in (".gcode.3mf", ".3mf", ".bgcode", ".gcode", ".gco"):
            if nome.lower().endswith(ext):
                nome = nome[: -len(ext)]
                break
        partes = [f"{os.path.basename(caminho)}: {fhoras(info['t'])}", f"{len(fil)} cor(es), {fpeso(sum(x[0] for x in fil))}"]
        if info.get("pecas"):
            partes.append(f"{info['pecas']} peça(s) na mesa")
        if info.get("placas", 1) > 1:
            partes.append(f"{info['placas']} mesas somadas")
        caixas = info.get("caixas") or []
        peca = (max(c[0] for c in caixas), max(c[1] for c in caixas)) if caixas else None
        alt = info.get("altura")
        mesa = info.get("mesa")
        aviso_mesa = None
        if mesa and (abs(mesa[0] - self.db.cfg("mesa_x")) > 1 or abs(mesa[1] - self.db.cfg("mesa_y")) > 1):
            aviso_mesa = (f"O arquivo foi fatiado para uma mesa de {fnum(mesa[0], 0)} × {fnum(mesa[1], 0)} mm, "
                          f"mas nas Configurações está {fnum(self.db.cfg('mesa_x'), 0)} × {fnum(self.db.cfg('mesa_y'), 0)} mm.")
        return dict(th=tmin // 60, tm=tmin % 60, ppm=info.get("pecas") or 1, nome=nome.replace("_", " "),
                    img=info.get("img"), peca_l=fnum(peca[0], 1).replace(",0", "") if peca else None,
                    peca_p=fnum(peca[1], 1).replace(",0", "") if peca else None,
                    peca_a=fnum(alt, 1).replace(",0", "") if alt else None, aviso_mesa=aviso_mesa,
                    cores=[dict(g=fnum(g, 1).replace(".", ""), hex=cor, rolo=C.rolo_parecido(self.db, tipo, cor))
                           for g, cor, tipo in fil],
                    resumo=" • ".join(partes))

    def arquivo_solto(self, nome):
        """Arquivo arrastado para a janela: o WebView2 entrega o caminho completo em _dnd_state."""
        from urllib.parse import unquote
        for _ in range(30):                                   # espera até ~1,5 s o caminho chegar
            for item in list(_dnd_state["paths"]):
                if unquote(item[0]) == nome:
                    _dnd_state["paths"].remove(item)
                    return self.importar(unquote(item[1]))
            time.sleep(0.05)
        return None                                           # a tela tenta ler o conteúdo direto

    def arquivo_conteudo(self, nome, b64):
        """Plano B: a tela manda o conteúdo do arquivo (quando o caminho não veio)."""
        import base64
        import tempfile
        pasta = tempfile.mkdtemp(prefix="ctrl3d_")
        caminho = os.path.join(pasta, os.path.basename(nome) or "arquivo.gcode")
        try:
            with open(caminho, "wb") as f:
                f.write(base64.b64decode(b64))
            res = self.importar(caminho)
            if isinstance(res, dict) and "resumo" in res:
                res["resumo"] = res["resumo"].replace(os.path.basename(caminho), os.path.basename(nome))
            return res
        finally:
            shutil.rmtree(pasta, ignore_errors=True)

    def escolher_arquivo(self):
        res = self._janela.create_file_dialog(
            webview.FileDialog.OPEN,
            file_types=("Arquivo fatiado (*.gcode;*.bgcode;*.3mf;*.gco)", "Todos os arquivos (*.*)"))
        caminho = _dialogo_caminho(res)
        return self.importar(caminho) if caminho else None

    def salvar_pedido(self, f):
        r, _ = self._calc(f)
        peca = (f.get("peca") or "").strip()
        if not peca:
            return dict(erro="Dê um nome para a peça antes de salvar.")
        det = dict(f)
        det.pop("img", None)                       # a foto fica só no catálogo
        det["consumo"] = {str(k): gr for k, gr in r["consumo"].items() if k != "padrao"}
        det["preco_un"], det["dias"] = r["preco_un"], r["dias_fila"]
        self.db.run("""INSERT INTO pedidos(data,cliente,peca,qtd,canal,horas,gramas,custo,preco,lucro,status,entrega,
                       detalhes,baixado) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,0)""",
                    (hoje().isoformat(), (f.get("cliente") or "").strip(), peca, r["qtd"], f.get("canal"),
                     r["h_total"], r["g_total"], r["custo"], r["total"], r["lucro"], "Orçamento",
                     r["pronto"].isoformat(), json.dumps(det, ensure_ascii=False)))
        return dict(ok=True)

    def registrar_pessoal(self, f):
        """Impressão para uso próprio: guarda o gasto e desconta o filamento do estoque."""
        r, _ = self._calc(f)
        if r["h_maq"] <= 0:
            return dict(erro="Coloque a peça (arquivo ou tempo) antes de registrar.")
        gasto = r["c_fil"] + r["c_ene"] + r["c_maq"]
        consumo = {str(k): gr for k, gr in r["consumo"].items() if k != "padrao"}
        self.db.run("INSERT INTO pessoal(data,peca,qtd,horas,gramas,gasto,detalhes) VALUES(?,?,?,?,?,?,?)",
                    (hoje().isoformat(), (f.get("peca") or "").strip() or "Peça 3D", r["qtd"], r["h_total"],
                     r["g_total"], gasto, json.dumps(dict(consumo=consumo), ensure_ascii=False)))
        if consumo:
            self.db.mexer_estoque(consumo, -1)
        return dict(ok=True, txt=f"Registrado: {brl(gasto)} • {fpeso(r['g_total'])}"
                                 + (" descontados do estoque" if consumo else ""))

    def pessoal(self, ym):
        tot, itens = C.dados_pessoal(self.db, ym)
        return dict(total=brl(tot["g"]), n=tot["n"], gramas=fpeso(tot["gr"]), horas=fhoras(tot["h"]),
                    itens=[dict(id=i["id"], data=data_br(i["data"])[:5], peca=i["peca"], qtd=i["qtd"],
                                gramas=fpeso(i["gramas"]), tempo=fhoras(i["horas"]), gasto=brl(i["gasto"]))
                           for i in itens])

    def excluir_pessoal(self, pid):
        r = self.db.one("SELECT * FROM pessoal WHERE id=?", (pid,))
        if r:
            consumo = json.loads(r["detalhes"] or "{}").get("consumo", {})
            if consumo:
                self.db.mexer_estoque(consumo, +1)          # devolve o filamento
            self.db.run("DELETE FROM pessoal WHERE id=?", (pid,))

    def copiar_orcamento(self, f):
        r, _ = self._calc(f)
        txt = C.texto_orcamento((f.get("cliente") or "").strip(), (f.get("peca") or "").strip() or "Peça 3D",
                                r["qtd"], r["preco_un"], r["total"], r["dias_fila"])
        copiar_texto(txt)
        return txt

    # ------------------------------------------------------------------ catálogo
    def salvar_produto(self, nome, dados, substituir=False):
        nome = (nome or "").strip()
        if not nome:
            return dict(erro="Escreva o nome da peça para salvar no catálogo.")
        if self.db.one("SELECT 1 FROM produtos WHERE nome=?", (nome,)) and not substituir:
            return dict(existe=True)
        self.db.run("INSERT INTO produtos(nome,dados) VALUES(?,?) ON CONFLICT(nome) DO UPDATE SET dados=excluded.dados",
                    (nome, json.dumps(dados, ensure_ascii=False)))
        return dict(ok=True)

    def produto(self, nome):
        r = self.db.one("SELECT dados FROM produtos WHERE nome=?", (nome,))
        if not r:
            return None
        d = json.loads(r["dados"])
        if d.get("cores") and isinstance(d["cores"][0], list):      # formato da versão anterior
            d["cores"] = [dict(rolo=c[0], g=c[1], hex=(c[2] if len(c) > 2 else "")) for c in d["cores"]]
        return d

    def catalogo(self, canal):
        canal_t = self._canal(canal)
        rolos = C.rolos_db(self.db)
        out = []
        for r in self.db.all("SELECT * FROM produtos ORDER BY nome"):
            d = self.produto(r["nome"])
            e = self._entrada(d)
            res = {q: C.calcular_pedido(self.db, dict(e, qtd=q), rolos, canal_t, 0) for q in (1, 10, 50, 100)}
            out.append(dict(id=r["id"], nome=r["nome"], tempo=fhoras(e["t_mesa"] / max(1, e["ppm"])),
                            ppm=res[100]["por_mesa"],
                            g=fpeso(res[1]["g_total"]), custo=brl(res[1]["custo"]),
                            precos=[brl(res[q]["preco_un"]) for q in (1, 10, 50, 100)],
                            prazo=f"{max(1, math.ceil(res[50]['dias']))} dia(s)",
                            cores=[c.get("hex") or "" for c in d.get("cores", [])], img=d.get("img")))
        return out

    def excluir_produto(self, pid):
        self.db.run("DELETE FROM produtos WHERE id=?", (pid,))

    # ------------------------------------------------------------------ pedidos e fila
    def pedidos(self, filtro="Todos", busca=""):
        prev, acum, hdia = C.previsoes_fila(self.db)
        busca = (busca or "").strip().lower()
        if prev:
            livre = hoje() + dt.timedelta(days=max(1, math.ceil(acum / hdia)))
            fila = dict(n=len(prev), h=fhoras(acum), dias=fnum(acum / hdia, 1), livre=livre.strftime("%d/%m"))
        else:
            fila = None
        linhas, soma = [], 0.0
        for r in self.db.all("SELECT * FROM pedidos ORDER BY id DESC"):
            st = r["status"]
            if filtro == "Em aberto" and st in ("Pago", "Cancelado", "Orçamento"):
                continue
            if filtro == "Na fila" and r["id"] not in prev:
                continue
            if filtro not in ("Todos", "Em aberto", "Na fila") and st != filtro:
                continue
            if busca and busca not in f"{r['cliente']} {r['peca']}".lower():
                continue
            if r["id"] in prev:
                pronto = data_br(prev[r["id"]])
            elif st == "Orçamento":
                d = hoje() + dt.timedelta(days=max(1, math.ceil((acum + r["horas"]) / hdia)))
                pronto = "se aprovar: " + d.strftime("%d/%m")
            elif st == "Pronto":
                pronto = "pronto ✓"
            else:
                pronto = "—"
            linhas.append(dict(id=r["id"], data=data_br(r["data"])[:5], cliente=r["cliente"] or "—", peca=r["peca"],
                               qtd=r["qtd"], tempo=fhoras(r["horas"]), preco=brl(r["preco"]), lucro=brl(r["lucro"]),
                               neg=r["lucro"] < 0, status=st, pronto=pronto))
            soma += r["preco"]
        return dict(fila=fila, linhas=linhas, total=f"{len(linhas)} pedido(s) • {brl(soma)}")

    def mudar_status(self, ids, novo):
        msgs = []
        for pid in ids:
            r = self.db.one("SELECT * FROM pedidos WHERE id=?", (pid,))
            consumo = json.loads(r["detalhes"] or "{}").get("consumo", {})
            baixado = r["baixado"]
            if novo in C.STATUS_BAIXA and not baixado and consumo:
                self.db.mexer_estoque(consumo, -1)
                baixado = 1
                msgs.append(f"Pedido #{pid}: filamento descontado do estoque.")
            elif novo in ("Orçamento", "Aprovado", "Cancelado") and baixado:
                self.db.mexer_estoque(consumo, +1)
                baixado = 0
                msgs.append(f"Pedido #{pid}: filamento devolvido ao estoque.")
            self.db.run("UPDATE pedidos SET status=?, baixado=? WHERE id=?", (novo, baixado, pid))
        return msgs

    def copiar_pedido(self, pid):
        r = self.db.one("SELECT * FROM pedidos WHERE id=?", (pid,))
        det = json.loads(r["detalhes"] or "{}")
        txt = C.texto_orcamento(r["cliente"], r["peca"], r["qtd"], det.get("preco_un", r["preco"] / max(1, r["qtd"])),
                                r["preco"], det.get("dias", 1))
        copiar_texto(txt)
        return txt

    def excluir_pedido(self, pid):
        r = self.db.one("SELECT * FROM pedidos WHERE id=?", (pid,))
        if r and r["baixado"] and r["status"] not in ("Entregue", "Pago", "Pronto"):
            self.db.mexer_estoque(json.loads(r["detalhes"] or "{}").get("consumo", {}), +1)
        self.db.run("DELETE FROM pedidos WHERE id=?", (pid,))

    # ------------------------------------------------------------------ filamentos
    def filamentos(self):
        tot_g = tot_v = 0.0
        for r in self.db.all("SELECT * FROM filamentos"):
            tot_g += r["restante_g"]
            tot_v += r["restante_g"] * (r["preco"] / r["peso_g"] if r["peso_g"] else 0)
        return dict(linhas=self._rolos(), total=f"{fpeso(tot_g)} em estoque • {brl(tot_v)}")

    def add_filamento(self, d):
        nome, preco, peso = (d.get("nome") or "").strip(), num(d.get("preco")), num(d.get("peso"), 1000)
        qtd = max(1, int(num(d.get("qtd"), 1)))
        marca, tipo, cor = (d.get("marca") or "").strip(), d.get("tipo") or "PLA", d.get("cor") or ""
        if not nome or preco <= 0 or peso <= 0:
            return dict(erro="Preencha nome, preço e peso do rolo.")
        ex = self.db.one("SELECT id FROM filamentos WHERE lower(nome)=lower(?) AND lower(marca)=lower(?) AND tipo=?",
                         (nome, marca, tipo))
        if ex:
            self.db.run("UPDATE filamentos SET restante_g=restante_g+?, preco=?, peso_g=?, cor=? WHERE id=?",
                        (peso * qtd, preco, peso, cor, ex["id"]))
        else:
            self.db.run("INSERT INTO filamentos(nome,marca,tipo,preco,peso_g,restante_g,cor) VALUES(?,?,?,?,?,?,?)",
                        (nome, marca, tipo, preco, peso, peso * qtd, cor))
        if d.get("lancar"):
            self.db.run("INSERT INTO gastos(data,categoria,descricao,valor) VALUES(?,?,?,?)",
                        (hoje().isoformat(), "Filamento", f"{qtd}x {nome} {tipo} {marca}".strip(), preco * qtd))
        return dict(ok=True)

    def ajustar_filamento(self, fid, gramas):
        self.db.run("UPDATE filamentos SET restante_g=? WHERE id=?", (max(0, num(gramas)), fid))

    def cor_filamento(self, fid, cor):
        self.db.run("UPDATE filamentos SET cor=? WHERE id=?", (cor, fid))

    def repor_filamento(self, fid, lancar):
        r = self.db.one("SELECT * FROM filamentos WHERE id=?", (fid,))
        self.db.run("UPDATE filamentos SET restante_g=restante_g+? WHERE id=?", (r["peso_g"], fid))
        if lancar:
            self.db.run("INSERT INTO gastos(data,categoria,descricao,valor) VALUES(?,?,?,?)",
                        (hoje().isoformat(), "Filamento", f"1x {r['nome']} {r['tipo']} {r['marca']}".strip(), r["preco"]))
        return f"+{fpeso(r['peso_g'])} de {r['nome']}"

    def excluir_filamento(self, fid):
        self.db.run("DELETE FROM filamentos WHERE id=?", (fid,))

    # ------------------------------------------------------------------ gastos
    def gastos(self, ym):
        linhas, tot = [], 0.0
        for r in self.db.all("SELECT * FROM gastos WHERE substr(data,1,7)=? ORDER BY data DESC, id DESC", (ym,)):
            linhas.append(dict(id=r["id"], data=data_br(r["data"]), cat=r["categoria"], desc=r["descricao"] or "",
                               valor=brl(r["valor"])))
            tot += r["valor"]
        return dict(linhas=linhas, total=brl(tot))

    def add_gasto(self, d):
        try:
            data = C.data_iso(d.get("data") or "")
        except ValueError:
            return dict(erro="Data inválida. Use dd/mm/aaaa.")
        v = num(d.get("valor"))
        if v <= 0:
            return dict(erro="Informe o valor do gasto.")
        self.db.run("INSERT INTO gastos(data,categoria,descricao,valor) VALUES(?,?,?,?)",
                    (data, d.get("cat"), (d.get("desc") or "").strip(), v))
        return dict(ok=True, ym=data[:7])

    def excluir_gasto(self, gid):
        self.db.run("DELETE FROM gastos WHERE id=?", (gid,))

    # ------------------------------------------------------------------ resumo
    def resumo(self, ym):
        p, rec, orc, gas, cats, top = C.dados_resumo(self.db, ym)
        return dict(
            tiles=[["Vendido", brl(p["v"]), f"{p['q']} peças"],
                   ["Recebido (pago)", brl(rec), f"a receber: {brl(p['v'] - rec)}"],
                   ["Gastos do mês", brl(gas), f"{len(cats)} categoria(s)"],
                   ["Saldo de caixa", brl(rec - gas), "recebido − gastos"],
                   ["Lucro dos pedidos", brl(p["l"]), f"custo: {brl(p['c'])}"],
                   ["Horas de impressão", fhoras(p["h"]), f"lucro/hora: {brl(p['l'] / p['h']) if p['h'] else '-'}"],
                   ["Filamento usado", fpeso(p["g"]), "nos pedidos do mês"],
                   ["Pedidos", str(p["n"]), f"+ {orc} orçamento(s) em aberto"]],
            pessoal=(lambda t: f"{t['n']} impressão(ões) • {brl(t['g'])} • {fpeso(t['gr'])} de filamento • {fhoras(t['h'])}"
                     if t["n"] else "")(C.dados_pessoal(self.db, ym)[0]),
            cats=[[c["categoria"], brl(c["v"])] for c in cats],
            top=[[t["peca"], f"{t['q']} un. • {brl(t['v'])}"] for t in top])

    def exportar_excel(self, ym):
        res = self._janela.create_file_dialog(webview.FileDialog.SAVE, save_filename=f"CTRL3D_{C.mes_br(ym).replace('/', '-')}.xlsx",
                                              file_types=("Planilha Excel (*.xlsx)",))
        arq = _dialogo_caminho(res)
        if not arq:
            return None
        if not arq.lower().endswith(".xlsx"):
            arq += ".xlsx"
        try:
            C.exportar_excel(self.db, ym, arq)
        except PermissionError:
            return dict(erro="Não consegui salvar. O arquivo está aberto no Excel?")
        os.startfile(arq)
        return dict(ok=True)

    # ------------------------------------------------------------------ configurações
    def salvar_config(self, d):
        for k, v in d.items():
            self.db.set_cfg(k, num(v))

    def add_canal(self, nome, pct, fixa):
        nome = (nome or "").strip()
        if not nome:
            return
        ex = self.db.one("SELECT id FROM canais WHERE nome=?", (nome,))
        if ex:
            self.db.run("UPDATE canais SET taxa_pct=?, taxa_fixa=? WHERE id=?", (num(pct), num(fixa), ex["id"]))
        else:
            self.db.run("INSERT INTO canais(nome,taxa_pct,taxa_fixa) VALUES(?,?,?)", (nome, num(pct), num(fixa)))

    def rem_canal(self, cid):
        self.db.run("DELETE FROM canais WHERE id=?", (cid,))

    def abrir_pasta(self):
        os.startfile(os.path.dirname(self.db.caminho))

    def backup(self):
        """Cópia dos dados em Dados\\Backups (um clique, com data e hora no nome)."""
        pasta = os.path.join(os.path.dirname(self.db.caminho), "Backups")
        os.makedirs(pasta, exist_ok=True)
        arq = os.path.join(pasta, f"ctrl3d_backup_{dt.datetime.now():%Y-%m-%d_%Hh%M}.db")
        self.db.c.commit()
        destino = C.sqlite3.connect(arq)
        with destino:
            self.db.c.backup(destino)                  # cópia segura mesmo com o programa aberto
        destino.close()
        os.startfile(pasta)
        return True

    def abrir_manual(self):
        manual = os.path.join(C.pasta_raiz(), "Documentacao", "Manual de uso.html")
        if os.path.exists(manual):
            os.startfile(manual)
            return True
        return False


# todas as chamadas da tela passam por uma trava (o banco é um só)
_trava = threading.RLock()     # RLock: uma função da tela pode chamar outra sem travar


def _travado(f):
    @functools.wraps(f)
    def w(*a, **k):
        with _trava:
            return f(*a, **k)
    return w


for _n, _f in list(vars(Api).items()):
    if callable(_f) and not _n.startswith("_"):
        setattr(Api, _n, _travado(_f))


def main():
    api = Api()
    janela = webview.create_window("CTRL 3D • Custos, prazos e pedidos", recurso(os.path.join("ui", "index.html")),
                                   js_api=api, width=1400, height=900, min_size=(1150, 720),
                                   background_color="#F4F5F8", maximized=True, text_select=False)
    api._janela = janela

    def mandar_arquivo(caminho):
        res = api.importar(caminho)
        janela.evaluate_js(f"App.aoImportar({json.dumps(res, ensure_ascii=False)})")

    # Liga a captura do caminho completo dos arquivos soltos (o WebView2 só guarda se houver "ouvinte").
    # A própria tela (app.js) avisa o Python quando um arquivo é solto e pede o caminho com arquivo_solto().
    _dnd_state["num_listeners"] += 1

    def ao_carregar():
        args = [a for a in sys.argv[1:] if os.path.isfile(a)]
        if args:
            mandar_arquivo(args[0])

    janela.events.loaded += ao_carregar
    webview.start(gui="edgechromium", debug=bool(os.environ.get("CTRL3D_DEBUG")))


if __name__ == "__main__":
    main()
