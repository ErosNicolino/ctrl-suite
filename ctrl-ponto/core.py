# -*- coding: utf-8 -*-
"""CTRL Ponto • núcleo: dados (dados.json, compatível com a versão anterior), jornadas, saldos, feriados e histórico.

Formato do dados.json (mantido da versão anterior; campos desconhecidos são preservados ao salvar):
  People   [{Id, Name, Role, Active}]
  Marks    [{Person, Date "AAAA-MM-DD", Times ["HH:mm"...] (até 4), ExpectedMinutes, Source, Review, Mode, Edited}]
  Scales   [{From "AAAA-MM-DD", Days [7 modos, domingo primeiro: 0 folga, 1 meia (04:00), 2 inteira (08:00)], Minutes [7] ou null}]
  Audit    ["data-hora-ISO texto", ...]
  Closed   ["AAAA-MM:IdDaPessoa", ...]
  Company, Cnpj, Address, Auto (fechamento automático), StartWithWindows
  Excecoes [{Pessoa ("" = todos), Data, Minutos, Motivo}]   (novo nesta versão)
"""
import os
import json
import uuid
import shutil
import datetime as dt

MODO_MIN = {0: 0, 1: 240, 2: 480}
DIAS_SEMANA = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"]
SEM_CURTO = ["dom.", "seg.", "ter.", "qua.", "qui.", "sex.", "sáb."]
NOMES_MARCA = ["Entrada", "Saída p/ almoço", "Volta do almoço", "Saída"]
MESES = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro",
         "novembro", "dezembro"]


def pasta_dados():
    return os.environ.get("CTRLPONTO_DIR") or os.path.join(os.environ["LOCALAPPDATA"], "CtrlPonto")


# ----------------------------------------------------------------- horas
def hm(minutos, sinal=False):
    """minutos -> 'HH:MM' (com + ou - se sinal=True)."""
    if minutos is None:
        return "—"
    s = "-" if minutos < 0 else ("+" if sinal and minutos > 0 else "")
    m = abs(int(round(minutos)))
    return f"{s}{m // 60:02d}:{m % 60:02d}"


def para_min(txt):
    """'HH:mm' -> minutos; None se inválido."""
    try:
        h, m = str(txt).strip().split(":")
        h, m = int(h), int(m)
        if 0 <= h < 24 and 0 <= m < 60:
            return h * 60 + m
    except (ValueError, AttributeError):
        pass
    return None


def agora():
    return dt.datetime.now()


def hoje():
    return dt.date.today()


# ----------------------------------------------------------------- feriados de São Paulo/SP
def pascoa(ano):
    a, b, c = ano % 19, ano // 100, ano % 100
    d, e = b // 4, b % 4
    f = (b + 8) // 25
    g = (b - f + 1) // 3
    h = (19 * a + b - d - g + 15) % 30
    i, k = c // 4, c % 4
    l = (32 + 2 * e + 2 * i - h - k) % 7
    m = (a + 11 * h + 22 * l) // 451
    mes = (h + l - 7 * m + 114) // 31
    dia = ((h + l - 7 * m + 114) % 31) + 1
    return dt.date(ano, mes, dia)


def feriados(ano):
    """Feriados nacionais, do estado de SP e da cidade de São Paulo (Carnaval e pontos facultativos não entram)."""
    p = pascoa(ano)
    f = {
        dt.date(ano, 1, 1): "Confraternização Universal",
        dt.date(ano, 1, 25): "Aniversário de São Paulo",
        p - dt.timedelta(days=2): "Sexta-feira Santa",
        dt.date(ano, 4, 21): "Tiradentes",
        dt.date(ano, 5, 1): "Dia do Trabalho",
        p + dt.timedelta(days=60): "Corpus Christi",
        dt.date(ano, 7, 9): "Revolução Constitucionalista",
        dt.date(ano, 9, 7): "Independência",
        dt.date(ano, 10, 12): "Nossa Senhora Aparecida",
        dt.date(ano, 11, 2): "Finados",
        dt.date(ano, 11, 15): "Proclamação da República",
        dt.date(ano, 11, 20): "Dia da Consciência Negra",
        dt.date(ano, 12, 25): "Natal",
    }
    return dict(sorted(f.items()))


# ----------------------------------------------------------------- dados
class Dados:
    def __init__(self, pasta=None):
        self.pasta = pasta or pasta_dados()
        self.arq = os.path.join(self.pasta, "dados.json")
        os.makedirs(self.pasta, exist_ok=True)
        self.carregar()

    def carregar(self):
        if os.path.exists(self.arq):
            with open(self.arq, encoding="utf-8-sig") as f:
                self.d = json.load(f)
        else:
            self.d = {}
        d = self.d
        for k in ("People", "Marks", "Scales", "Audit", "Closed", "Exceptions", "Excecoes"):
            d.setdefault(k, [])
        d.setdefault("Company", "")
        d.setdefault("Cnpj", "")
        d.setdefault("Address", "")
        d.setdefault("Auto", True)
        d.setdefault("StartWithWindows", False)
        d.setdefault("AjusteEntradaMin", 3)       # entrada conta X minutos antes (chegar e ligar o PC)
        if not d["Scales"]:
            d["Scales"].append({"From": "0001-01-01", "Days": [0, 2, 2, 2, 2, 2, 1], "Minutes": None})
        self._mtime = os.path.getmtime(self.arq) if os.path.exists(self.arq) else 0

    def recarregar_se_mudou(self):
        """Outro programa (ou a versão antiga) mexeu no arquivo? Lê de novo antes de alterar."""
        if os.path.exists(self.arq) and os.path.getmtime(self.arq) != self._mtime:
            self.carregar()

    def salvar(self):
        tmp = self.arq + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(self.d, f, ensure_ascii=False, indent=2)
        os.replace(tmp, self.arq)
        self._mtime = os.path.getmtime(self.arq)
        self.backup_do_dia()

    def backup_do_dia(self):
        pasta = os.path.join(self.pasta, "backups")
        os.makedirs(pasta, exist_ok=True)
        shutil.copy2(self.arq, os.path.join(pasta, f"dados-{hoje().isoformat()}.json"))
        antigos = sorted(n for n in os.listdir(pasta) if n.startswith("dados-") and n.endswith(".json"))
        for n in antigos[:-120]:                      # guarda os últimos 120 dias
            os.remove(os.path.join(pasta, n))

    def historico(self, texto):
        self.d["Audit"].append(f"{agora().astimezone().isoformat(timespec='microseconds')} {texto}")

    # --------------------------------------------------------------- pessoas
    def pessoas(self, so_ativas=False):
        return [p for p in self.d["People"] if p.get("Active", True) or not so_ativas]

    def pessoa(self, pid):
        return next((p for p in self.d["People"] if p["Id"] == pid), None)

    def add_pessoa(self, nome, cargo):
        p = {"Id": str(uuid.uuid4()), "Name": nome.strip(), "Role": cargo.strip(), "Active": True}
        self.d["People"].append(p)
        self.historico(f"Colaborador cadastrado: {p['Name']} ({p['Role']})")
        self.salvar()
        return p

    def editar_pessoa(self, pid, nome, cargo, ativo):
        p = self.pessoa(pid)
        antes = f"{p['Name']} / {p['Role']} / {'ativo' if p.get('Active', True) else 'arquivado'}"
        p.update(Name=nome.strip(), Role=cargo.strip(), Active=bool(ativo))
        self.historico(f"Colaborador {pid} Antes={antes} Depois={p['Name']} / {p['Role']} / "
                       f"{'ativo' if ativo else 'arquivado'}")
        self.salvar()

    # --------------------------------------------------------------- meta do dia
    def escala_em(self, data):
        iso = data.isoformat()
        validas = [s for s in self.d["Scales"] if s.get("From", "0001-01-01") <= iso]
        return max(validas, key=lambda s: s["From"]) if validas else self.d["Scales"][0]

    def meta_escala(self, data):
        """Meta pela escala da semana (sem feriado/exceção)."""
        s = self.escala_em(data)
        i = (data.weekday() + 1) % 7                    # domingo = 0
        mins = s.get("Minutes")
        if isinstance(mins, list) and len(mins) == 7 and mins[i] is not None:
            return int(mins[i])
        return MODO_MIN.get(s["Days"][i], 0)

    def excecao(self, pid, data):
        iso = data.isoformat()
        for e in self.d["Excecoes"]:
            if e["Data"] == iso and e.get("Pessoa", "") in ("", pid):
                return e
        return None

    def meta_dia(self, pid, data):
        """(minutos, motivo) — exceção > feriado > escala."""
        e = self.excecao(pid, data)
        if e:
            return int(e["Minutos"]), e.get("Motivo") or "Mudança de jornada"
        fer = feriados(data.year).get(data)
        if fer:
            return 0, fer
        m = self.meta_escala(data)
        return m, ("Folga" if m == 0 else "")

    # --------------------------------------------------------------- marcações
    def marca(self, pid, data):
        iso = data.isoformat()
        return next((m for m in self.d["Marks"] if m["Person"] == pid and m["Date"] == iso), None)

    def dia(self, pid, data):
        """Situação completa de um dia."""
        m = self.marca(pid, data)
        meta_atual, motivo = self.meta_dia(pid, data)
        tempos = list(m["Times"]) if m else []
        meta = int(m["ExpectedMinutes"]) if m and m.get("ExpectedMinutes") is not None else meta_atual
        mins = [para_min(t) for t in tempos]
        trab = 0
        for a, b in zip(mins[0::2], mins[1::2]):
            if a is not None and b is not None and b > a:
                trab += b - a
        n = len(tempos)
        completo = n == 4 or (n == 2 and meta <= 360)
        if n == 0:
            if meta == 0:
                sit, tom = (motivo or "Folga"), "neutral"
            elif data < hoje():
                sit, tom = "Sem marcação", "danger"
            elif data == hoje():
                sit, tom = "Aguardando entrada", "info"
            else:
                sit, tom = "—", "neutral"
        elif completo:
            sit, tom = "Completo", "ok"
        elif data == hoje():
            sit, tom = ("Em almoço" if n == 2 else "Trabalhando"), "info"
        else:
            sit, tom = "Incompleto", "warn"
        aberto = None                                    # trabalhando agora: conta até este minuto
        if data == hoje() and n % 2 == 1 and mins[-1] is not None:
            aberto = max(0, agora().hour * 60 + agora().minute - mins[-1])
        return dict(data=data.isoformat(), dia=data.day, sem=SEM_CURTO[(data.weekday() + 1) % 7], tempos=tempos,
                    meta=meta, trabalhado=trab, aberto=aberto, completo=completo,
                    saldo=(trab - meta) if completo else None, situacao=sit, tom=tom, motivo=motivo,
                    editado=bool(m and m.get("Edited")), origem=(m or {}).get("Source", ""),
                    pendente=(meta > 0 and not completo and data < hoje()) or (0 < n < 4 and not completo and data < hoje()))

    def _nomes_do_dia(self, pid, data):
        """Meia jornada (até 6 h): só entrada e saída. Jornada inteira: os quatro horários."""
        m = self.marca(pid, data)
        meta = m["ExpectedMinutes"] if m and m.get("ExpectedMinutes") is not None else self.meta_dia(pid, data)[0]
        return ["Entrada", "Saída"] if meta <= 360 else NOMES_MARCA

    def proxima_marca(self, pid):
        """Nome do próximo registro de hoje, ou None se o dia já está completo."""
        m = self.marca(pid, hoje())
        n = len(m["Times"]) if m else 0
        nomes = self._nomes_do_dia(pid, hoje())
        return nomes[n] if n < len(nomes) else None

    def registrar(self, pid):
        self.recarregar_se_mudou()
        d = hoje()
        clique = agora()
        hora = clique.strftime("%H:%M")
        m = self.marca(pid, d)
        ajuste = 0
        if not (m and m["Times"]):                     # entrada do dia: conta alguns minutos antes
            ajuste = max(0, min(15, int(self.d.get("AjusteEntradaMin", 3) or 0)))
            if ajuste and clique.hour * 60 + clique.minute >= ajuste:
                hora = (clique - dt.timedelta(minutes=ajuste)).strftime("%H:%M")
            else:
                ajuste = 0
        meta, _ = self.meta_dia(pid, d)
        if m is None:
            m = {"Person": pid, "Date": d.isoformat(), "Times": [], "ExpectedMinutes": meta, "Source": "",
                 "Review": "", "Mode": 2 if meta > 240 else (1 if meta > 0 else 0), "Edited": False}
            self.d["Marks"].append(m)
        nomes = self._nomes_do_dia(pid, d)
        if len(m["Times"]) >= len(nomes):
            raise ValueError("O dia de hoje já está completo. Para mudar, use Folha mensal › Editar dia.")
        if m["Times"] and para_min(m["Times"][-1]) is not None and para_min(hora) <= para_min(m["Times"][-1]):
            raise ValueError(f"Já existe um registro às {m['Times'][-1]}. Espere um minuto para registrar de novo.")
        qual = nomes[len(m["Times"])]
        m["Times"].append(hora)
        self.historico(f"Registro {pid} {d.isoformat()} {hora}"
                       + (f" (clique às {clique:%H:%M}; entrada conta {ajuste} min antes)" if ajuste else ""))
        self.salvar()
        return qual, hora

    def editar_dia(self, pid, data_iso, tempos, meta_min, motivo):
        self.recarregar_se_mudou()
        motivo = (motivo or "").strip()
        if not motivo:
            raise ValueError("Escreva o motivo da alteração.")
        tempos = [t.strip() for t in tempos if t and t.strip()]
        mins = [para_min(t) for t in tempos]
        if any(x is None for x in mins):
            raise ValueError("Use o formato HH:mm (ex.: 08:05).")
        if any(b <= a for a, b in zip(mins, mins[1:])):
            raise ValueError("Os horários precisam estar em ordem crescente, no mesmo dia.")
        if len(tempos) > 4:
            raise ValueError("No máximo 4 horários por dia.")
        tempos = [f"{x // 60:02d}:{x % 60:02d}" for x in mins]
        data = dt.date.fromisoformat(data_iso)
        m = self.marca(pid, data)
        antes = ",".join(m["Times"]) if m else ""
        meta_antes = m.get("ExpectedMinutes") if m else self.meta_dia(pid, data)[0]
        if m is None:
            m = {"Person": pid, "Date": data_iso, "Times": [], "ExpectedMinutes": meta_min, "Source": "",
                 "Review": "", "Mode": 2 if meta_min > 240 else (1 if meta_min > 0 else 0), "Edited": True}
            self.d["Marks"].append(m)
        m["Times"] = tempos
        m["ExpectedMinutes"] = int(meta_min)
        m["Edited"] = True
        self.historico(f"Ajuste {pid} {data_iso} Antes={antes} Depois={','.join(tempos)} "
                       f"MetaAntes={meta_antes} MetaDepois={int(meta_min)} Motivo={motivo}")
        if f"{data_iso[:7]}:{pid}" in self.d["Closed"]:
            self.historico(f"Mês {data_iso[:7]} já estava fechado para {pid}: gere o PDF de novo.")
        self.salvar()

    # --------------------------------------------------------------- mês
    def mes(self, pid, ano, mes):
        d = dt.date(ano, mes, 1)
        dias = []
        while d.month == mes:
            dias.append(self.dia(pid, d))
            d += dt.timedelta(days=1)
        completos = [x for x in dias if x["completo"]]
        trab = sum(x["trabalhado"] for x in completos)
        meta = sum(x["meta"] for x in completos)
        return dict(dias=dias, trabalhado=trab, meta=meta, saldo=trab - meta, completos=len(completos),
                    positivo=sum(x["saldo"] for x in completos if x["saldo"] > 0),
                    compensar=-sum(x["saldo"] for x in completos if x["saldo"] < 0),
                    pendencias=sum(1 for x in dias if x["pendente"]),
                    registrado=sum(x["trabalhado"] for x in dias),
                    fechado=f"{ano:04d}-{mes:02d}:{pid}" in self.d["Closed"])

    def saldo_total(self, pid):
        """Saldo acumulado de todos os dias completos."""
        tot = 0
        for m in self.d["Marks"]:
            if m["Person"] == pid:
                x = self.dia(pid, dt.date.fromisoformat(m["Date"]))
                if x["completo"]:
                    tot += x["saldo"]
        return tot

    def pode_fechar(self, pid, ano, mes):
        r = self.mes(pid, ano, mes)
        return r["pendencias"] == 0

    def marcar_fechado(self, pid, ano, mes, arquivo):
        chave = f"{ano:04d}-{mes:02d}:{pid}"
        if chave not in self.d["Closed"]:
            self.d["Closed"].append(chave)
        self.historico(f"Fechamento {chave} PDF={os.path.basename(arquivo)}")
        self.salvar()

    def meses_para_fechar(self):
        """Meses que já terminaram (ou último dia após 20h), sem pendências e ainda não fechados."""
        if not self.d.get("Auto", True):
            return []
        out = []
        h = hoje()
        ult_dia = (dt.date(h.year + (h.month == 12), h.month % 12 + 1, 1) - dt.timedelta(days=1))
        for p in self.pessoas(so_ativas=True):
            meses = sorted({m["Date"][:7] for m in self.d["Marks"] if m["Person"] == p["Id"]})
            for ym in meses:
                ano, mes = int(ym[:4]), int(ym[5:7])
                terminou = (ano, mes) < (h.year, h.month) or (h == ult_dia and agora().hour >= 20 and (ano, mes) == (h.year, h.month))
                if terminou and f"{ym}:{p['Id']}" not in self.d["Closed"] and self.pode_fechar(p["Id"], ano, mes):
                    out.append(dict(pessoa=p["Id"], ano=ano, mes=mes))
        return out

    # --------------------------------------------------------------- escala e exceções
    def salvar_escala(self, minutos7):
        """Nova escala a partir de hoje (as jornadas já iniciadas guardam a meta delas)."""
        dias = [2 if m > 240 else (1 if m > 0 else 0) for m in minutos7]
        iso = hoje().isoformat()
        self.d["Scales"] = [s for s in self.d["Scales"] if s.get("From") != iso]
        self.d["Scales"].append({"From": iso, "Days": dias, "Minutes": [int(m) for m in minutos7]})
        self.historico("Escala alterada a partir de " + iso + ": " +
                       ", ".join(f"{DIAS_SEMANA[i][:3]} {hm(m)}" for i, m in enumerate(minutos7)))
        self.salvar()

    def add_excecao(self, pessoa, data_iso, minutos, motivo):
        self.d["Excecoes"] = [e for e in self.d["Excecoes"] if not (e["Data"] == data_iso and e.get("Pessoa", "") == pessoa)]
        self.d["Excecoes"].append({"Pessoa": pessoa, "Data": data_iso, "Minutos": int(minutos), "Motivo": motivo.strip()})
        self.historico(f"Mudança de jornada {data_iso} {pessoa or 'todos'} meta={hm(minutos)} {motivo.strip()}")
        self.salvar()

    def rem_excecao(self, pessoa, data_iso):
        self.d["Excecoes"] = [e for e in self.d["Excecoes"] if not (e["Data"] == data_iso and e.get("Pessoa", "") == pessoa)]
        self.historico(f"Mudança de jornada removida {data_iso} {pessoa or 'todos'}")
        self.salvar()
