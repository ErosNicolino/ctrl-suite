# -*- coding: utf-8 -*-
"""CTRL Ponto • janela (interface em HTML no padrão visual do ERP), ícone na bandeja e PDF da folha."""
import os
import sys
import json
import time
import shutil
import ctypes
import threading
import functools
import subprocess
import datetime as dt

import webview

import core as C
from core import hm, para_min, hoje, agora

NOME_APP = "CTRL Ponto"
CHAVE_RUN = r"Software\Microsoft\Windows\CurrentVersion\Run"


def recurso(rel):
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, rel)


def exe_atual():
    return sys.executable if getattr(sys, "frozen", False) else os.path.abspath(__file__)


def programa_antigo_aberto():
    """A versão anterior (PontoCtrl.exe) está aberta? As duas não podem gravar ao mesmo tempo."""
    try:
        r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq PontoCtrl.exe", "/NH"], capture_output=True, text=True,
                           creationflags=0x08000000)
        return "PontoCtrl.exe" in r.stdout
    except OSError:
        return False


class Api:
    def __init__(self):
        self.D = C.Dados()
        self._janela = None
        self.sair = None              # definido pelo main (encerrar de verdade)

    # ------------------------------------------------------------------ geral
    def _nome(self, pid):
        p = self.D.pessoa(pid)
        return p["Name"] if p else pid

    def estado(self):
        self.D.recarregar_se_mudou()
        d = self.D.d
        foto = os.path.join(self.D.pasta, "folha-original-outubro-2026.jpeg")
        return dict(
            pessoas=[dict(id=p["Id"], nome=p["Name"], cargo=p.get("Role", ""), ativo=p.get("Active", True))
                     for p in d["People"]],
            empresa=dict(nome=d.get("Company", ""), cnpj=d.get("Cnpj", ""), endereco=d.get("Address", "")),
            auto=d.get("Auto", True), ajuste_entrada=int(d.get("AjusteEntradaMin", 3) or 0),
            iniciar=self.iniciar_com_windows_ativo(),
            antigo=programa_antigo_aberto(), foto=os.path.exists(foto), pasta=self.D.pasta,
            hoje=hoje().isoformat(), mes=hoje().strftime("%Y-%m"))

    # ------------------------------------------------------------------ início / registrar
    def painel(self, pid):
        self.D.recarregar_se_mudou()
        x = self.D.dia(pid, hoje())
        trab = x["trabalhado"] + (x["aberto"] or 0)
        falta = max(0, x["meta"] - trab) if x["meta"] else 0
        r = self.D.mes(pid, hoje().year, hoje().month)
        equipe = []
        for p in self.D.pessoas(so_ativas=True):
            y = self.D.dia(p["Id"], hoje())
            equipe.append(dict(id=p["Id"], nome=p["Name"], cargo=p.get("Role", ""), situacao=y["situacao"], tom=y["tom"],
                               ultimo=y["tempos"][-1] if y["tempos"] else "—", tempos=y["tempos"]))
        nomes = self.D._nomes_do_dia(pid, hoje())
        return dict(
            dia=x, proxima=self.D.proxima_marca(pid), nomes=nomes,
            trabalhado=hm(trab), meta=hm(x["meta"]), falta=hm(falta),
            saldo_dia=hm(x["saldo"], True) if x["saldo"] is not None else None,
            previsao=self._previsao_saida(x),
            mes=dict(saldo=hm(r["saldo"], True), neg=r["saldo"] < 0, trabalhado=hm(r["registrado"]),
                     pendencias=r["pendencias"], completos=r["completos"]),
            total=hm(self.D.saldo_total(pid), True), equipe=equipe, agora=agora().strftime("%H:%M:%S"),
            data=f"{C.DIAS_SEMANA[(hoje().weekday() + 1) % 7]}, {hoje().day} de {C.MESES[hoje().month - 1]}")

    def _previsao_saida(self, x):
        """Que horas cumpre a meta de hoje (com o almoço já feito ou previsto de 1 h)."""
        t = [para_min(v) for v in x["tempos"]]
        if not t or not x["meta"] or x["completo"]:
            return None
        feito = x["trabalhado"]
        if len(t) == 1:
            almoco = 60 if x["meta"] > 360 else 0
            return hm(t[0] + x["meta"] + almoco)
        if len(t) == 3:
            return hm(t[2] + (x["meta"] - feito))
        return None

    def registrar(self, pid):
        try:
            qual, hora = self.D.registrar(pid)
        except ValueError as e:
            return dict(erro=str(e))
        return dict(ok=True, txt=f"{qual} registrada às {hora} — {self._nome(pid)}")

    # ------------------------------------------------------------------ folha mensal
    def folha(self, pid, ym):
        ano, mes = int(ym[:4]), int(ym[5:7])
        r = self.D.mes(pid, ano, mes)
        p = self.D.pessoa(pid) or {}
        linhas = []
        for x in r["dias"]:
            t = x["tempos"] + [""] * (4 - len(x["tempos"]))
            meia = x["meta"] <= 360 and len(x["tempos"]) <= 2 and x["tempos"]
            if meia:                                    # meia jornada: entrada e saída nas pontas
                t = [x["tempos"][0], "", "", x["tempos"][1] if len(x["tempos"]) > 1 else ""]
            linhas.append(dict(data=x["data"], dia=f"{x['dia']:02d}", sem=x["sem"], t=t,
                               horas=hm(x["trabalhado"]) if x["tempos"] else "—",
                               meta=hm(x["meta"]) if x["meta"] else "—",
                               saldo=hm(x["saldo"], True) if x["saldo"] is not None else "—",
                               neg=(x["saldo"] or 0) < 0, pos=(x["saldo"] or 0) > 0,
                               situacao=x["situacao"], tom=x["tom"], editado=x["editado"],
                               folga=x["meta"] == 0 and not x["tempos"], meta_min=x["meta"], tempos=x["tempos"]))
        d = self.D.d
        return dict(
            linhas=linhas, nome=p.get("Name", ""), cargo=p.get("Role", ""),
            titulo=f"{C.MESES[mes - 1].upper()} / {ano}",
            trabalhado=hm(r["registrado"]), completos_h=hm(r["trabalhado"]), meta=hm(r["meta"]),
            saldo=hm(r["saldo"], True), neg=r["saldo"] < 0, positivo=hm(r["positivo"]), compensar=hm(r["compensar"]),
            completos=r["completos"], pendencias=r["pendencias"], fechado=r["fechado"],
            empresa=dict(nome=d.get("Company", ""), cnpj=d.get("Cnpj", ""), endereco=d.get("Address", "")),
            emitido=agora().strftime("%d/%m/%Y %H:%M"))

    def editar_dia(self, pid, data_iso, tempos, meta_hm, motivo):
        meta = para_min(meta_hm) if meta_hm else 0
        if meta_hm and meta is None:
            return dict(erro="Meta no formato HH:mm (ex.: 08:00).")
        try:
            self.D.editar_dia(pid, data_iso, tempos, meta or 0, motivo)
        except ValueError as e:
            return dict(erro=str(e))
        return dict(ok=True)

    # ------------------------------------------------------------------ equipe
    def add_pessoa(self, nome, cargo):
        if not (nome or "").strip():
            return dict(erro="Escreva o nome.")
        p = self.D.add_pessoa(nome, cargo or "")
        return dict(ok=True, id=p["Id"])

    def editar_pessoa(self, pid, nome, cargo, ativo):
        if not (nome or "").strip():
            return dict(erro="Escreva o nome.")
        self.D.editar_pessoa(pid, nome, cargo or "", ativo)
        return dict(ok=True)

    # ------------------------------------------------------------------ escala, feriados, exceções
    def escala(self):
        h = hoje()
        dom = h - dt.timedelta(days=(h.weekday() + 1) % 7)
        return [dict(dia=C.DIAS_SEMANA[i].capitalize(), meta=hm(self.D.meta_escala(dom + dt.timedelta(days=i))))
                for i in range(7)]

    def salvar_escala(self, metas):
        mins = [para_min(m) if m else 0 for m in metas]
        if any(m is None for m in mins):
            return dict(erro="Use HH:mm em todas as metas (ex.: 08:00, 04:00 ou 00:00).")
        self.D.salvar_escala(mins)
        return dict(ok=True)

    def feriados(self, ano):
        return [dict(data=d.strftime("%d/%m"), sem=C.SEM_CURTO[(d.weekday() + 1) % 7], nome=n, passou=d < hoje())
                for d, n in C.feriados(int(ano)).items()]

    def excecoes(self):
        return [dict(data=e["Data"], data_br=dt.date.fromisoformat(e["Data"]).strftime("%d/%m/%Y"),
                     pessoa=e.get("Pessoa", ""), nome=self._nome(e["Pessoa"]) if e.get("Pessoa") else "Todos",
                     meta=hm(e["Minutos"]), motivo=e.get("Motivo", ""))
                for e in sorted(self.D.d["Excecoes"], key=lambda e: e["Data"], reverse=True)]

    def add_excecao(self, pessoa, data_br, meta_hm, motivo):
        try:
            data = dt.datetime.strptime(data_br.strip(), "%d/%m/%Y").date()
        except ValueError:
            return dict(erro="Data no formato dd/mm/aaaa.")
        m = para_min(meta_hm or "00:00")
        if m is None:
            return dict(erro="Meta no formato HH:mm (00:00 = folga).")
        if not (motivo or "").strip():
            return dict(erro="Escreva o motivo (ex.: folga combinada, trabalho no feriado).")
        self.D.add_excecao(pessoa or "", data.isoformat(), m, motivo)
        return dict(ok=True)

    def rem_excecao(self, pessoa, data_iso):
        self.D.rem_excecao(pessoa or "", data_iso)

    # ------------------------------------------------------------------ histórico
    def historico(self, busca=""):
        nomes = {p["Id"]: p["Name"] for p in self.D.d["People"]}
        busca = (busca or "").strip().lower()
        out = []
        for linha in reversed(self.D.d["Audit"]):
            quando, _, txt = linha.partition(" ")
            for pid, nome in nomes.items():
                txt = txt.replace(pid, nome)
            if busca and busca not in txt.lower():
                continue
            try:
                q = dt.datetime.fromisoformat(quando[:26] + quando[-6:] if len(quando) > 32 else quando)
                quando_br = q.strftime("%d/%m/%Y %H:%M")
            except ValueError:
                quando_br = quando[:16].replace("T", " ")
            tipo = ("Registro" if txt.startswith("Registro") else "Ajuste" if txt.startswith("Ajuste") else
                    "Fechamento" if txt.startswith("Fechamento") else "Outro")
            out.append(dict(quando=quando_br, txt=txt, tipo=tipo))
            if len(out) >= 400:
                break
        return out

    # ------------------------------------------------------------------ configurações
    def salvar_config(self, cfg):
        d = self.D.d
        d["Company"], d["Cnpj"], d["Address"] = cfg.get("nome", ""), cfg.get("cnpj", ""), cfg.get("endereco", "")
        d["Auto"] = bool(cfg.get("auto"))
        try:
            d["AjusteEntradaMin"] = max(0, min(15, int(cfg.get("ajuste_entrada") or 0)))
        except ValueError:
            pass
        self.D.historico("Configurações alteradas")
        self.D.salvar()
        return dict(ok=True)

    def iniciar_com_windows_ativo(self):
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, CHAVE_RUN) as k:
                v, _ = winreg.QueryValueEx(k, NOME_APP)
            return os.path.normcase(exe_atual()) in os.path.normcase(v)
        except OSError:
            return False

    def iniciar_com_windows(self, ligar):
        """Liga/desliga o início automático DESTA versão (substitui a entrada da versão anterior)."""
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, CHAVE_RUN, 0, winreg.KEY_SET_VALUE) as k:
            if ligar:
                winreg.SetValueEx(k, NOME_APP, 0, winreg.REG_SZ, f'"{exe_atual()}" --tray')
            else:
                try:
                    winreg.DeleteValue(k, NOME_APP)
                except OSError:
                    pass
        self.D.d["StartWithWindows"] = bool(ligar)
        self.D.salvar()
        return self.iniciar_com_windows_ativo()

    def fechar_antigo(self):
        subprocess.run(["taskkill", "/IM", "PontoCtrl.exe", "/F"], capture_output=True, creationflags=0x08000000)
        time.sleep(1)
        self.D.carregar()
        return not programa_antigo_aberto()

    def abrir_pasta(self, qual=""):
        p = os.path.join(self.D.pasta, qual) if qual else self.D.pasta
        os.makedirs(p, exist_ok=True)
        os.startfile(p)

    def abrir_foto(self):
        f = os.path.join(self.D.pasta, "folha-original-outubro-2026.jpeg")
        if os.path.exists(f):
            os.startfile(f)

    def backup(self):
        self.D.backup_do_dia()
        self.abrir_pasta("backups")
        return True

    def abrir_manual(self):
        raiz = os.path.dirname(exe_atual()) if getattr(sys, "frozen", False) else os.path.dirname(os.path.dirname(exe_atual()))
        f = os.path.join(raiz, "Documentacao", "Manual de uso.html")
        if os.path.exists(f):
            os.startfile(f)
            return True
        return False

    def sair_programa(self):
        if self.sair:
            threading.Thread(target=self.sair, daemon=True).start()

    # ------------------------------------------------------------------ PDF (a mesma folha da impressão)
    def _cdp(self, metodo, params):
        from System import Action
        form = self._janela.native
        wv = form.browser.webview
        caixa = {}

        def chamar():
            caixa["t"] = wv.CoreWebView2.CallDevToolsProtocolMethodAsync(metodo, json.dumps(params))
        form.Invoke(Action(chamar))
        t = caixa["t"]
        t.Wait()
        return json.loads(t.Result)

    def gerar_pdf(self, pid, ym, fechar=False, abrir=True):
        import base64
        p = self.D.pessoa(pid) or {"Name": "colaborador"}
        pasta = os.path.join(self.D.pasta, "relatorios")
        os.makedirs(pasta, exist_ok=True)
        nome = f"{ym}-{p['Name']}-{pid[:8]}.pdf"
        arq = os.path.join(pasta, nome)
        r = self._cdp("Page.printToPDF", dict(printBackground=True, preferCSSPageSize=True, paperWidth=8.27,
                                              paperHeight=11.69, marginTop=0, marginBottom=0, marginLeft=0, marginRight=0))
        dados = base64.b64decode(r["data"])
        try:
            with open(arq, "wb") as f:
                f.write(dados)
        except PermissionError:                         # PDF anterior aberto: salva outra versão
            arq = arq[:-4] + f" ({agora():%Y-%m-%d %Hh%M}).pdf"
            with open(arq, "wb") as f:
                f.write(dados)
        if fechar:
            with _trava:
                self.D.marcar_fechado(pid, int(ym[:4]), int(ym[5:7]), arq)
        if abrir:
            os.startfile(arq)
        return dict(ok=True, arquivo=os.path.basename(arq))

    def meses_para_fechar(self):
        return [dict(pessoa=x["pessoa"], ym=f"{x['ano']:04d}-{x['mes']:02d}") for x in self.D.meses_para_fechar()]

    def fechar_mes(self, pid, ym):
        ano, mes = int(ym[:4]), int(ym[5:7])
        if not self.D.pode_fechar(pid, ano, mes):
            return dict(erro="Ainda há dias pendentes nesse mês. Ajuste em Editar dia antes de fechar.")
        return dict(ok=True)


_trava = threading.RLock()


def _travado(f):
    @functools.wraps(f)
    def w(*a, **k):
        with _trava:
            return f(*a, **k)
    return w


for _n, _f in list(vars(Api).items()):
    if callable(_f) and not _n.startswith("_") and _n not in ("gerar_pdf",):
        setattr(Api, _n, _travado(_f))


# ---------------------------------------------------------------------- uma janela só + bandeja
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.CreateMutexW.restype = ctypes.c_void_p
k32.CreateEventW.restype = ctypes.c_void_p
k32.OpenEventW.restype = ctypes.c_void_p
EVENTO = "Local\\CtrlPonto2_Mostrar"


def ja_aberto():
    k32.CreateMutexW(None, False, "Local\\CtrlPonto2_Unico")
    return ctypes.get_last_error() == 183           # ERROR_ALREADY_EXISTS


def avisar_instancia():
    h = k32.OpenEventW(0x0002, False, EVENTO)        # EVENT_MODIFY_STATE
    if h:
        k32.SetEvent(ctypes.c_void_p(h))


def main():
    if ja_aberto():
        avisar_instancia()                           # abrir de novo só mostra a janela que já está rodando
        return
    evento = k32.CreateEventW(None, False, False, EVENTO)
    na_bandeja = "--tray" in sys.argv
    api = Api()
    janela = webview.create_window("CTRL Ponto • Controle de jornada", recurso(os.path.join("ui", "index.html")),
                                   js_api=api, width=1320, height=860, min_size=(1100, 700),
                                   background_color="#F7F9F8", maximized=not na_bandeja, hidden=na_bandeja,
                                   text_select=False)
    api._janela = janela
    estado = {"saindo": False, "icone": None}

    def mostrar(pagina=None):
        janela.show()
        janela.restore()
        if pagina:
            janela.evaluate_js(f"App.ir({json.dumps(pagina)})")

    def sair():
        estado["saindo"] = True
        if estado["icone"]:
            estado["icone"].stop()
        janela.destroy()
    api.sair = sair

    def ao_fechar():
        if estado["saindo"] or not estado["icone"]:
            return True
        janela.hide()                                # o X esconde na bandeja; Sair pelo menu encerra
        return False
    janela.events.closing += ao_fechar

    def bandeja():
        try:
            import pystray
            from PIL import Image
            img = Image.open(recurso(os.path.join("assets", "icon.png")))
            menu = pystray.Menu(
                pystray.MenuItem("Abrir CTRL Ponto", lambda: mostrar(), default=True),
                pystray.MenuItem("Registrar ponto", lambda: mostrar("inicio")),
                pystray.MenuItem("Folha mensal", lambda: mostrar("folha")),
                pystray.Menu.SEPARATOR,
                pystray.MenuItem("Sair do CTRL Ponto", lambda: sair()))
            estado["icone"] = pystray.Icon("CtrlPonto", img, "CTRL Ponto", menu)
            estado["icone"].run()
        except Exception:
            estado["icone"] = None

    def esperar_outra_instancia():
        while not estado["saindo"]:
            if k32.WaitForSingleObject(ctypes.c_void_p(evento), 1000) == 0:
                mostrar()

    def fechamento_automatico():
        time.sleep(20)
        while not estado["saindo"]:
            try:
                janela.evaluate_js("App.fechamentoAutomatico()")
            except Exception:
                pass
            time.sleep(600)

    def iniciar():
        threading.Thread(target=bandeja, daemon=True).start()
        threading.Thread(target=esperar_outra_instancia, daemon=True).start()
        threading.Thread(target=fechamento_automatico, daemon=True).start()

    webview.start(iniciar, gui="edgechromium", debug=bool(os.environ.get("CTRLPONTO_DEBUG")))


if __name__ == "__main__":
    main()
