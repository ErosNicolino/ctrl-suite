"use strict";
/* CTRL Ponto • interface. Contas e dados ficam no Python (core.py). Visual: erp.css (igual ao ERP). */

let api = null, E = null;
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const ICO = {
  lapis: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21.17 6.81a1 1 0 0 0-3.98-3.98L3.84 16.17a2 2 0 0 0-.5.83l-1.32 4.35a.5.5 0 0 0 .62.62l4.35-1.32a2 2 0 0 0 .83-.5z"/></svg>',
  lixo: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>',
  vazio: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/></svg>',
};

/* ------------------------------------------------------------------ avisos e diálogos (padrão do ERP) */
function toast(msg, tipo = "") {
  const t = document.createElement("div");
  t.className = "toast " + tipo;
  t.textContent = msg;
  $("#toasts").append(t);
  setTimeout(() => { t.style.transition = "opacity .3s"; t.style.opacity = 0; setTimeout(() => t.remove(), 300); }, 3600);
}

/* dialogo({titulo, sub, texto, form(html), ok, perigo, largo, aoAbrir(el), validar() -> erro|null}) */
function dialogo({ titulo, sub = "", texto = "", form = "", ok = "OK", cancelar = "Cancelar", perigo = false, largo = false, aoAbrir, validar, input = null }) {
  return new Promise(res => {
    $("#m-tit").textContent = titulo;
    $("#m-sub").textContent = sub;
    $("#m-txt").textContent = texto;
    $("#m-form").innerHTML = form;
    $("#m-info").innerHTML = "";
    const inp = $("#m-inp");
    inp.hidden = input === null;
    if (input !== null) inp.value = input;
    $("#m-sim").textContent = ok;
    $("#m-sim").className = "btn" + (perigo ? " perigo-forte" : "");
    $("#modal").className = "modal" + (perigo ? " perigo" : "") + (largo ? " largo" : "");
    $("#m-nao").textContent = cancelar || "";
    $("#m-nao").hidden = cancelar === null;
    $("#modal-fundo").classList.add("on");
    if (aoAbrir) aoAbrir($("#m-form"));
    setTimeout(() => {
      const primeiro = $("#m-form input, #m-form select");
      (input !== null ? inp : perigo ? $("#m-nao") : primeiro || $("#m-sim")).focus();
    }, 30);
    const fim = v => {
      $("#modal-fundo").classList.remove("on");
      $("#m-sim").onclick = $("#m-nao").onclick = null;
      document.removeEventListener("keydown", tecla, true);
      res(v);
    };
    const confirmar = async () => {
      if (validar) { const erro = await validar($("#m-form")); if (erro) return toast(erro, "aviso"); }
      fim(input !== null ? inp.value : true);
    };
    const tecla = e => {
      if (e.key === "Escape") { e.preventDefault(); fim(input !== null ? null : false); }
      if (e.key === "Enter" && !perigo && e.target.tagName !== "TEXTAREA") { e.preventDefault(); confirmar(); }
    };
    document.addEventListener("keydown", tecla, true);
    $("#m-sim").onclick = confirmar;
    $("#m-nao").onclick = () => fim(input !== null ? null : false);
  });
}

async function chama(nome, ...args) {
  try { return await api[nome](...args); }
  catch (e) { toast("Algo deu errado: " + (e && e.message ? e.message : e), "erro"); throw e; }
}

const tag = (txt, tom) => `<span class="tag ${tom}">${esc(txt)}</span>`;
const minutos = t => { const m = /^(\d{1,2}):(\d{2})$/.exec((t || "").trim()); return m && +m[1] < 24 && +m[2] < 60 ? +m[1] * 60 + +m[2] : null; };
const hm = (m, sinal) => m == null ? "—" : (m < 0 ? "-" : sinal ? "+" : "") + String(Math.floor(Math.abs(m) / 60)).padStart(2, "0") + ":" + String(Math.abs(m) % 60).padStart(2, "0");
/* digitar 0905 vira 09:05 */
function mascaraHora(el) {
  el.addEventListener("input", () => {
    const d = el.value.replace(/\D/g, "").slice(0, 4);
    el.value = d.length > 2 ? d.slice(0, 2) + ":" + d.slice(2) : d;
  });
}

/* ------------------------------------------------------------------ navegação */
let pagina = "inicio";
const CARREGAR = { inicio: carregarInicio, folha: carregarFolha, equipe: carregarEquipe, escala: carregarEscala, historico: carregarHistorico, config: carregarConfig };
async function ir(p) {
  pagina = p;
  $$(".lateral [data-p]").forEach(b => b.classList.toggle("ativo", b.dataset.p === p));
  $$(".pagina").forEach(s => s.classList.toggle("ativa", s.id === "pg-" + p));
  $("#main").scrollTop = 0;
  await atualizarEstado();
  await CARREGAR[p]();
}
async function atualizarEstado() {
  E = await chama("estado");
  $("#aviso-antigo").hidden = !E.antigo;
}
const ativos = () => E.pessoas.filter(p => p.ativo);
let pessoaAtual = null;
function garantirPessoa() {
  if (!pessoaAtual || !E.pessoas.some(p => p.id === pessoaAtual)) pessoaAtual = (ativos()[0] || E.pessoas[0] || {}).id || null;
  return pessoaAtual;
}

/* ================================================================== REGISTRAR PONTO */
let P = null;
async function carregarInicio() {
  const pid = garantirPessoa();
  const box = $("#i-pessoas");
  if (!pid) {
    box.innerHTML = "";
    $("#i-bater").disabled = true;
    $("#i-bater-txt").textContent = "Cadastre um colaborador";
    $("#i-linha").innerHTML = "";
    $("#i-equipe").innerHTML = `<tr><td class="vazio-tab">${ICO.vazio}<b>Nenhum colaborador</b>Cadastre em Equipe.</td></tr>`;
    return;
  }
  const lista = ativos();
  box.innerHTML = lista.length <= 4
    ? `<div class="segmentado">${lista.map(p => `<button data-id="${p.id}" class="${p.id === pid ? "on" : ""}">${esc(p.nome.split(" ")[0])}</button>`).join("")}</div>`
    : `<select class="inp">${lista.map(p => `<option value="${p.id}" ${p.id === pid ? "selected" : ""}>${esc(p.nome)}</option>`).join("")}</select>`;
  $$("button", box).forEach(b => (b.onclick = () => { pessoaAtual = b.dataset.id; carregarInicio(); }));
  const sel = $("select", box); if (sel) sel.onchange = () => { pessoaAtual = sel.value; carregarInicio(); };

  P = await chama("painel", pid);
  $("#i-data").textContent = P.data.charAt(0).toUpperCase() + P.data.slice(1);
  const b = $("#i-bater");
  b.disabled = !P.proxima;
  $("#i-bater-txt").textContent = P.proxima ? `Registrar ${P.proxima.toLowerCase()}` : "Dia completo";
  $("#i-ajuste").textContent = P.proxima === "Entrada" && E.ajuste_entrada
    ? `A entrada conta ${E.ajuste_entrada} min antes do clique (tempo de chegar e ligar o PC).` : "";
  const t = P.dia.tempos;
  $("#i-linha").style.setProperty("--n", P.nomes.length);
  $("#i-linha").innerHTML = P.nomes.map((n, i) => `<div class="marco ${t[i] ? "feito" : i === t.length && P.proxima ? "agora" : "vazio"}"><small>${esc(n)}</small><b>${t[i] || "—"}</b></div>`).join("");
  $("#k-trab").textContent = P.trabalhado;
  $("#k-trab-s").textContent = P.dia.meta ? `meta de hoje: ${P.meta}` : (P.dia.motivo || "folga");
  $("#k-falta").textContent = P.dia.completo ? (P.saldo_dia || "00:00") : P.falta;
  $("#k-falta").className = "v" + (P.dia.completo && P.saldo_dia && P.saldo_dia.startsWith("-") ? " neg" : P.dia.completo ? " pos" : "");
  $("#k-falta-s").textContent = P.dia.completo ? "saldo de hoje (dia fechado)" : P.previsao ? `cumpre a meta às ${P.previsao}`
    : t.length === 2 && P.nomes.length === 4 ? `no almoço desde ${t[1]} — a previsão volta na volta do almoço`
    : P.dia.meta ? "após a entrada mostra a previsão" : "sem jornada hoje";
  $("#k-mes").textContent = P.mes.saldo;
  $("#k-mes").className = "v " + (P.mes.neg ? "neg" : "pos");
  $("#k-mes-s").textContent = `${P.mes.completos} dia(s) completo(s)` + (P.mes.pendencias ? ` • ${P.mes.pendencias} pendência(s)` : "");
  $("#k-total").textContent = P.total;
  $("#k-total").className = "v " + (P.total.startsWith("-") ? "neg" : "pos");
  const badge = $("#badge-pend"); badge.hidden = !P.mes.pendencias; badge.textContent = P.mes.pendencias;
  const trab = P.equipe.filter(x => x.situacao === "Trabalhando").length;
  $("#chip-txt").textContent = trab ? `${trab} trabalhando agora` : "Ninguém trabalhando agora";
  $("#chip-ponto").className = "ponto " + (trab ? "ok" : "");
  $("#i-equipe-n").textContent = `${P.equipe.length} ativo(s)`;
  $("#i-equipe").innerHTML = `<thead><tr><th>Colaborador</th><th>Horários de hoje</th><th>Situação</th></tr></thead><tbody>` +
    P.equipe.map(x => `<tr data-id="${x.id}" style="cursor:pointer"><td><b>${esc(x.nome)}</b><div class="nota">${esc(x.cargo)}</div></td>
      <td class="mut" style="font-variant-numeric:tabular-nums">${x.tempos.length ? x.tempos.join(" • ") : "—"}</td><td>${tag(x.situacao, x.tom)}</td></tr>`).join("") + "</tbody>";
  $$("#i-equipe tr[data-id]").forEach(tr => (tr.onclick = () => { pessoaAtual = tr.dataset.id; carregarInicio(); }));
}
function relogio() {
  const a = new Date();
  $("#i-relogio").textContent = a.toLocaleTimeString("pt-BR");
}
async function bater() {
  const pid = garantirPessoa();
  if (!pid || !P || !P.proxima) return;
  if (pagina !== "inicio") await ir("inicio");
  const r = await chama("registrar", pid);
  if (r.erro) return toast(r.erro, "aviso");
  toast(r.txt, "ok");
  carregarInicio();
}

/* ================================================================== FOLHA MENSAL */
let F = null;
function opcoesPessoas(sel, comTodos = false) {
  sel.innerHTML = (comTodos ? `<option value="">Todos</option>` : "") + E.pessoas.map(p => `<option value="${p.id}">${esc(p.nome)}${p.ativo ? "" : " (arquivado)"}</option>`).join("");
}
async function carregarFolha() {
  const pid = garantirPessoa();
  opcoesPessoas($("#f-pessoa"));
  if (!pid) { $("#f-tab").innerHTML = `<tr><td class="vazio-tab">${ICO.vazio}<b>Nenhum colaborador</b></td></tr>`; return; }
  $("#f-pessoa").value = pid;
  if (!$("#f-mes").value) $("#f-mes").value = E.mes;
  const ym = $("#f-mes").value;
  F = await chama("folha", pid, ym);
  $("#f-foto").hidden = !(E.foto && ym === "2026-10");
  const fe = $("#f-fechado");
  fe.hidden = false;
  fe.className = "tag " + (F.fechado ? "ok" : F.pendencias ? "warn" : "neutral");
  fe.textContent = F.fechado ? "Mês fechado" : F.pendencias ? `${F.pendencias} pendência(s)` : "Em aberto";
  const neg = F.neg ? " neg" : " pos";
  $("#f-kpis").innerHTML = [
    ["info", "Horas registradas", F.trabalhado, "todas as marcações"],
    ["marca", "Meta (dias completos)", F.meta, `${F.completos} dia(s) completo(s)`],
    ["ok", "Saldo positivo", F.positivo, "a mais nas jornadas"],
    ["warn", "Horas a compensar", F.compensar, "a menos nas jornadas"],
    ["violet", "Saldo do mês", `<span class="${neg}">${F.saldo}</span>`, F.pendencias ? `${F.pendencias} pendência(s)` : "sem pendências"],
  ].map(([c, t, v, s]) => `<div class="kpi ${c}"><div><small>${t}</small><div class="v">${v}</div><div class="s">${s}</div></div></div>`).join("");
  const hojeIso = E.hoje;
  $("#f-tab").innerHTML = `<thead><tr><th>Dia</th><th class="cen">Entrada</th><th class="cen">Saída almoço</th><th class="cen">Volta almoço</th><th class="cen">Saída</th>
      <th class="num">Horas</th><th class="num">Meta</th><th class="num">Saldo</th><th>Situação</th><th></th></tr></thead><tbody>` +
    F.linhas.map((l, i) => `<tr data-i="${i}" class="${l.folga ? "folga" : ""} ${l.data === hojeIso ? "hoje" : ""}">
      <td><b>${l.dia}</b> <span class="mut">${l.sem}</span></td>
      ${l.t.map(x => `<td class="cen h">${x || '<span class="mut">—</span>'}</td>`).join("")}
      <td class="num"><b>${l.horas}</b></td><td class="num mut">${l.meta}</td>
      <td class="num ${l.pos ? "pos" : l.neg ? "neg" : ""}">${l.saldo}</td>
      <td>${tag(l.situacao, l.tom)}${l.editado ? '<span class="ed" title="Ajuste manual">*</span>' : ""}</td>
      <td class="fim"><button class="icone" title="Editar dia">${ICO.lapis}</button></td></tr>`).join("") + "</tbody>";
  $$("#f-tab tr[data-i]").forEach(tr => (tr.onclick = () => editarDia(F.linhas[Number(tr.dataset.i)])));
  renderFolhaA4(F);
}

async function editarDia(l) {
  const pid = garantirPessoa();
  const nomes = ["Entrada", "Saída p/ almoço", "Volta do almoço", "Saída"];
  const t = l.tempos.length <= 2 && l.meta_min <= 360 && l.tempos.length ? [l.tempos[0], "", "", l.tempos[1] || ""] : l.tempos.concat(["", "", "", ""]).slice(0, 4);
  const [a, m, d] = l.data.split("-");
  const form = `<div class="grade" style="--cols:4">${nomes.map((n, i) => `<div class="campo"><label>${n}</label><input class="inp" data-t="${i}" value="${t[i] || ""}" placeholder="HH:mm"></div>`).join("")}</div>
    <div class="grade" style="--cols:4; margin-top:12px"><div class="campo"><label>Meta do dia</label><input class="inp" id="ed-meta" value="${hm(l.meta_min)}"></div>
    <div class="campo" style="grid-column:span 3"><label>Motivo da alteração (obrigatório)</label><input class="inp" id="ed-motivo" placeholder="Ex.: esqueceu de bater a volta do almoço"></div></div>
    <div class="junto" style="margin-top:10px"><button class="link" id="ed-limpar" type="button">Limpar marcações</button><span class="nota">O histórico guarda os valores antigos, os novos e o motivo.</span></div>`;
  const calcular = el => {
    const v = $$("[data-t]", el).map(i => minutos(i.value)).filter(x => x != null);
    let trab = 0; for (let i = 0; i + 1 < v.length; i += 2) trab += Math.max(0, v[i + 1] - v[i]);
    const meta = minutos($("#ed-meta", el).value) || 0;
    const completo = v.length === 4 || (v.length === 2 && meta <= 360);
    $("#m-info").innerHTML = `Trabalhado <b>${hm(trab)}</b> • meta <b>${hm(meta)}</b> • saldo <b>${completo ? hm(trab - meta, true) : "—"}</b>`;
  };
  const ok = await dialogo({
    titulo: `Editar ${d}/${m}/${a}`, sub: `${F.nome} • ${l.sem} • ${l.situacao}`, form, ok: "Salvar alteração", largo: true,
    aoAbrir: el => {
      $$("[data-t], #ed-meta", el).forEach(i => { mascaraHora(i); i.addEventListener("input", () => calcular(el)); });
      $("#ed-limpar", el).onclick = () => { $$("[data-t]", el).forEach(i => (i.value = "")); calcular(el); };
      calcular(el);
    },
    validar: async el => {
      if (!$("#ed-motivo", el).value.trim()) return "Escreva o motivo da alteração.";
      const tempos = $$("[data-t]", el).map(i => i.value);
      const r = await chama("editar_dia", pid, l.data, tempos, $("#ed-meta", el).value, $("#ed-motivo", el).value);
      return r.erro || null;
    },
  });
  if (ok) { toast("Dia ajustado e registrado no histórico.", "ok"); if (F.fechado) toast("Esse mês já estava fechado: gere o PDF de novo.", "aviso"); carregarFolha(); }
}

/* folha A4: a mesma para PDF e impressão */
function renderFolhaA4(f) {
  const vazio = '<span style="color:#93A1A7">—</span>';
  $("#folha-print").innerHTML = `<div class="a4">
    <div class="cab"><img src="logo.png"><div><h1>Folha mensal de ponto</h1><div class="mes">${esc(f.titulo)}</div><div class="sub">CONTROLE INDIVIDUAL DE JORNADA</div></div></div>
    <div class="faixa"><i></i><i></i><i></i><i></i></div>
    <div class="emp"><b>${esc(f.empresa.nome)}</b><div>${[f.empresa.cnpj && "CNPJ: " + f.empresa.cnpj, f.empresa.endereco].filter(Boolean).map(esc).join("  |  ")}</div></div>
    <div class="colab"><div><small>COLABORADOR</small><b>${esc(f.nome)}</b></div><div style="min-width:60mm"><small>CARGO</small>${esc(f.cargo)}</div></div>
    <table><thead><tr><th>DIA</th><th>ENTRADA</th><th>ALMOÇO<br>SAÍDA</th><th>ALMOÇO<br>VOLTA</th><th>SAÍDA</th><th>HORAS</th><th>META</th><th>SALDO</th><th>SITUAÇÃO</th></tr></thead><tbody>
    ${f.linhas.map(l => `<tr class="${l.folga ? "folga" : ""}"><td>${l.dia} ${l.sem.replace(".", "")}</td>${l.t.map(x => `<td>${x || vazio}</td>`).join("")}
      <td class="b">${l.horas}</td><td>${l.meta}</td><td class="${l.pos ? "pos" : l.neg ? "neg" : "b"}">${l.saldo}</td><td>${esc(l.situacao)}${l.editado ? " *" : ""}</td></tr>`).join("")}
    </tbody></table>
    <div class="tot"><div><small>HORAS REGISTRADAS</small><b>${f.trabalhado}</b></div><div><small>SALDO POSITIVO</small><b>${f.positivo}</b></div>
      <div><small>HORAS A COMPENSAR</small><b>${f.compensar}</b></div><div><small>SALDO DO MÊS</small><b class="${f.neg ? "neg" : "pos"}">${f.saldo}</b></div></div>
    <div class="obs">${f.completos} dias completos • ${f.pendencias} pendência(s) • Saldo calculado somente para jornadas completas.<br>* Ajuste manual. Saldo = horas registradas menos a meta do dia. Intervalos são descontados.</div>
    <div class="ass"><div>Assinatura do colaborador</div><div>Responsável pela empresa</div></div>
    <div class="rod"><span>Emitido em ${f.emitido} • CTRL Ponto</span><span>A4 • 1 / 1</span></div></div>`;
}
async function gerarPdf(pid, ym, fechar = false, abrir = true) {
  const f = await chama("folha", pid, ym);
  renderFolhaA4(f);
  await new Promise(r => setTimeout(r, 150));
  return chama("gerar_pdf", pid, ym, fechar, abrir);
}

/* ================================================================== EQUIPE */
async function carregarEquipe() {
  const t = $("#e-tab");
  if (!E.pessoas.length) { t.innerHTML = `<tr><td class="vazio-tab">${ICO.vazio}<b>Nenhum colaborador</b>Clique em Adicionar colaborador.</td></tr>`; return; }
  t.innerHTML = `<thead><tr><th>Nome</th><th>Cargo</th><th>Situação</th><th></th></tr></thead><tbody>` +
    E.pessoas.map(p => `<tr><td><b>${esc(p.nome)}</b></td><td>${esc(p.cargo)}</td><td>${tag(p.ativo ? "Ativo" : "Arquivado", p.ativo ? "ok" : "neutral")}</td>
      <td class="fim"><button class="btn soft peq" data-folha="${p.id}">Folha</button><button class="icone" data-ed="${p.id}" title="Editar">${ICO.lapis}</button></td></tr>`).join("") + "</tbody>";
  $$("[data-ed]", t).forEach(b => (b.onclick = () => formPessoa(E.pessoas.find(p => p.id === b.dataset.ed))));
  $$("[data-folha]", t).forEach(b => (b.onclick = () => { pessoaAtual = b.dataset.folha; ir("folha"); }));
}
async function formPessoa(p = null) {
  const form = `<div class="grade"><div class="campo"><label>Nome completo</label><input class="inp" id="p-nome" value="${esc(p ? p.nome : "")}"></div>
    <div class="campo"><label>Cargo</label><input class="inp" id="p-cargo" value="${esc(p ? p.cargo : "")}"></div></div>
    ${p ? `<label class="switch" style="margin-top:14px"><input type="checkbox" id="p-ativo" ${p.ativo ? "checked" : ""}><span>Ativo (desligue para arquivar)</span></label>` : ""}`;
  const ok = await dialogo({
    titulo: p ? "Editar colaborador" : "Adicionar colaborador", form, ok: p ? "Salvar" : "Adicionar",
    validar: async el => {
      const nome = $("#p-nome", el).value, cargo = $("#p-cargo", el).value;
      const r = p ? await chama("editar_pessoa", p.id, nome, cargo, $("#p-ativo", el).checked) : await chama("add_pessoa", nome, cargo);
      if (!r.erro && !p) pessoaAtual = r.id;
      return r.erro || null;
    },
  });
  if (ok) { toast("Equipe atualizada.", "ok"); await atualizarEstado(); carregarEquipe(); }
}

/* ================================================================== ESCALA, FERIADOS, EXCEÇÕES */
async function carregarEscala() {
  const esc7 = await chama("escala");
  $("#s-escala").innerHTML = esc7.map((d, i) => `<div class="campo"><label>${d.dia}</label><input class="inp" data-dia="${i}" value="${d.meta}"></div>`).join("");
  $$("[data-dia]").forEach(mascaraHora);
  const ano = new Date().getFullYear();
  if (!$("#s-ano").options.length) $("#s-ano").innerHTML = [ano - 1, ano, ano + 1].map(a => `<option ${a === ano ? "selected" : ""}>${a}</option>`).join("");
  const fer = await chama("feriados", $("#s-ano").value);
  $("#s-feriados").innerHTML = fer.map(f => `<div class="ln ${f.passou ? "passou" : ""}"><span><b>${f.data}</b> <span class="mut">${f.sem}</span></span><span>${esc(f.nome)}</span></div>`).join("");
  opcoesPessoas($("#x-pessoa"), true);
  const ex = await chama("excecoes");
  $("#x-tab").innerHTML = ex.length ? `<thead><tr><th>Data</th><th>Colaborador</th><th class="num">Meta</th><th>Motivo</th><th></th></tr></thead><tbody>` +
    ex.map(e => `<tr><td>${e.data_br}</td><td>${esc(e.nome)}</td><td class="num">${e.meta}</td><td>${esc(e.motivo)}</td>
      <td class="fim"><button class="icone del" data-rem="${e.data}" data-p="${e.pessoa}" title="Remover">${ICO.lixo}</button></td></tr>`).join("") + "</tbody>"
    : `<tr><td class="vazio-tab">Nenhuma mudança de jornada cadastrada.</td></tr>`;
  $$("[data-rem]").forEach(b => (b.onclick = async () => {
    if (await dialogo({ titulo: "Remover mudança de jornada?", texto: "A meta desse dia volta a seguir a escala e os feriados.", ok: "Remover", perigo: true })) {
      await chama("rem_excecao", b.dataset.p, b.dataset.rem); carregarEscala();
    }
  }));
}

/* ================================================================== HISTÓRICO */
let tipoHist = "";
async function carregarHistorico() {
  const linhas = (await chama("historico", $("#h-busca").value)).filter(l => !tipoHist || l.tipo === tipoHist);
  const tom = { Registro: "info", Ajuste: "warn", Fechamento: "ok", Outro: "neutral" };
  $("#h-tab").innerHTML = linhas.length ? `<thead><tr><th style="width:150px">Quando</th><th style="width:120px">Tipo</th><th>O que aconteceu</th></tr></thead><tbody>` +
    linhas.map(l => `<tr><td class="mut">${l.quando}</td><td>${tag(l.tipo, tom[l.tipo])}</td><td>${esc(l.txt)}</td></tr>`).join("") + "</tbody>"
    : `<tr><td class="vazio-tab">Nada encontrado.</td></tr>`;
}

/* ================================================================== CONFIGURAÇÕES */
async function carregarConfig() {
  $("#c-nome").value = E.empresa.nome; $("#c-cnpj").value = E.empresa.cnpj; $("#c-end").value = E.empresa.endereco;
  $("#c-ajuste").value = E.ajuste_entrada; $("#c-auto").checked = E.auto; $("#c-iniciar").checked = E.iniciar;
  $("#c-pasta").textContent = "Pasta de dados: " + E.pasta;
}

/* ================================================================== fechamento automático (chamado pelo Python a cada 10 min) */
window.App = {
  ir,
  async fechamentoAutomatico() {
    const lista = await chama("meses_para_fechar");
    for (const x of lista) {
      const r = await gerarPdf(x.pessoa, x.ym, true, false);
      if (r && r.ok) toast(`Mês ${x.ym.slice(5)}/${x.ym.slice(0, 4)} fechado: ${r.arquivo}`, "ok");
    }
    if (lista.length && pagina === "folha") carregarFolha();
  },
};

/* ================================================================== paleta Ctrl+K */
const ACOES = [
  { g: "Telas", t: "Registrar ponto", a: () => ir("inicio"), k: "F2" },
  { g: "Telas", t: "Folha mensal", a: () => ir("folha") },
  { g: "Telas", t: "Equipe", a: () => ir("equipe") },
  { g: "Telas", t: "Escala e feriados", a: () => ir("escala") },
  { g: "Telas", t: "Histórico de alterações", a: () => ir("historico") },
  { g: "Telas", t: "Configurações", a: () => ir("config") },
  { g: "Ações", t: "Adicionar colaborador", a: () => ir("equipe").then(() => formPessoa()) },
  { g: "Ações", t: "Gerar PDF do mês", a: () => ir("folha").then(() => $("#f-pdf").click()) },
  { g: "Ações", t: "Fazer backup agora", a: () => chama("backup") },
  { g: "Ações", t: "Manual de uso", a: () => chama("abrir_manual") },
];
let pSel = 0, pLista = [];
const normal = s => s.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
function abrirPaleta() { $("#paleta-fundo").classList.add("on"); $("#paleta-inp").value = ""; filtrarPaleta(); setTimeout(() => $("#paleta-inp").focus(), 20); }
function fecharPaleta() { $("#paleta-fundo").classList.remove("on"); }
function filtrarPaleta() {
  const q = normal($("#paleta-inp").value.trim());
  const pessoas = (E ? E.pessoas : []).map(p => ({ g: "Folha de", t: p.nome, a: () => { pessoaAtual = p.id; ir("folha"); } }));
  pLista = ACOES.concat(pessoas).filter(x => !q || normal(x.t).includes(q)).slice(0, 40);
  pSel = 0;
  let g = "";
  $("#paleta-itens").innerHTML = pLista.map((x, i) => (x.g !== g ? `<div class="grupo">${esc(g = x.g)}</div>` : "") +
    `<div class="it ${i ? "" : "sel"}" data-i="${i}"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>${esc(x.t)}${x.k ? `<small>${x.k}</small>` : ""}</div>`).join("") || `<div class="vazio-tab">Nada encontrado.</div>`;
  $$("#paleta-itens .it").forEach(el => (el.onclick = () => executar(Number(el.dataset.i))));
}
function executar(i) { const x = pLista[i]; fecharPaleta(); if (x) x.a(); }
function mover(d) {
  if (!pLista.length) return;
  pSel = (pSel + d + pLista.length) % pLista.length;
  $$("#paleta-itens .it").forEach(el => el.classList.toggle("sel", Number(el.dataset.i) === pSel));
  const s = $(`#paleta-itens .it[data-i="${pSel}"]`); if (s) s.scrollIntoView({ block: "nearest" });
}

/* ================================================================== início */
function ligar() {
  $$(".lateral [data-p]").forEach(b => (b.onclick = () => ir(b.dataset.p)));
  $("#bt-ajuda").onclick = async () => { if (!(await chama("abrir_manual"))) toast("Manual não encontrado na pasta Documentacao.", "aviso"); };
  $("#bt-comando").onclick = abrirPaleta;
  $("#bt-registrar-topo").onclick = () => (pagina === "inicio" ? bater() : ir("inicio"));
  $("#chip-agora").onclick = () => ir("inicio");
  $("#i-bater").onclick = bater;
  $("#bt-fechar-antigo").onclick = async () => {
    if (!(await dialogo({ titulo: "Fechar a versão antiga?", texto: "A versão antiga do CTRL Ponto vai ser encerrada (ela fica escondida perto do relógio). Os dados continuam os mesmos.", ok: "Fechar a antiga" }))) return;
    toast((await chama("fechar_antigo")) ? "Versão antiga fechada." : "Não consegui fechar. Use o ícone dela perto do relógio › Sair.", "ok");
    await atualizarEstado(); CARREGAR[pagina]();
  };
  // folha
  $("#f-pessoa").onchange = () => { pessoaAtual = $("#f-pessoa").value; carregarFolha(); };
  $("#f-mes").onchange = carregarFolha;
  const mudaMes = d => { const [a, m] = $("#f-mes").value.split("-").map(Number); const x = new Date(a, m - 1 + d, 1); $("#f-mes").value = `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, "0")}`; carregarFolha(); };
  $("#f-ant").onclick = () => mudaMes(-1); $("#f-prox").onclick = () => mudaMes(1);
  $("#f-pdf").onclick = async () => { const r = await gerarPdf(garantirPessoa(), $("#f-mes").value); if (r && r.ok) toast("PDF salvo: " + r.arquivo, "ok"); };
  $("#f-imprimir").onclick = () => { renderFolhaA4(F); setTimeout(() => window.print(), 100); };
  $("#f-foto").onclick = () => chama("abrir_foto");
  // equipe
  $("#e-add").onclick = () => formPessoa();
  // escala
  $("#s-ano").onchange = carregarEscala;
  $("#s-salvar").onclick = async () => {
    const r = await chama("salvar_escala", $$("[data-dia]").map(i => i.value));
    if (r.erro) return toast(r.erro, "aviso");
    toast("Escala salva a partir de hoje.", "ok"); carregarEscala();
  };
  mascaraHora($("#x-meta"));
  $("#x-data").addEventListener("input", e => { const d = e.target.value.replace(/\D/g, "").slice(0, 8); e.target.value = [d.slice(0, 2), d.slice(2, 4), d.slice(4)].filter(Boolean).join("/"); });
  $("#x-add").onclick = async () => {
    const r = await chama("add_excecao", $("#x-pessoa").value, $("#x-data").value, $("#x-meta").value || "00:00", $("#x-motivo").value);
    if (r.erro) return toast(r.erro, "aviso");
    ["#x-data", "#x-meta", "#x-motivo"].forEach(s => ($(s).value = "")); toast("Mudança de jornada salva.", "ok"); carregarEscala();
  };
  // histórico
  let tb; $("#h-busca").oninput = () => { clearTimeout(tb); tb = setTimeout(carregarHistorico, 200); };
  $$("#h-tipo button").forEach(b => (b.onclick = () => { tipoHist = b.dataset.t; $$("#h-tipo button").forEach(x => x.classList.toggle("on", x === b)); carregarHistorico(); }));
  // configurações
  mascaraHora; $("#c-salvar").onclick = async () => {
    await chama("salvar_config", { nome: $("#c-nome").value, cnpj: $("#c-cnpj").value, endereco: $("#c-end").value, auto: $("#c-auto").checked, ajuste_entrada: $("#c-ajuste").value });
    await atualizarEstado(); toast("Configurações salvas.", "ok");
  };
  $("#c-iniciar").onchange = async e => {
    const on = e.target.checked;
    if (on && !(await dialogo({ titulo: "Abrir com o Windows?", texto: "Ao ligar o computador, esta versão vai abrir escondida perto do relógio, no lugar da versão antiga.", ok: "Ligar" }))) { e.target.checked = false; return; }
    e.target.checked = await chama("iniciar_com_windows", on);
    toast(e.target.checked ? "Vai abrir com o Windows." : "Não abre mais com o Windows.", "ok");
  };
  $("#c-abrir").onclick = () => chama("abrir_pasta", "");
  $("#c-relatorios").onclick = () => chama("abrir_pasta", "relatorios");
  $("#c-backup").onclick = async () => { await chama("backup"); toast("Backup do dia salvo.", "ok"); };
  // paleta e atalhos
  $("#paleta-inp").oninput = filtrarPaleta;
  $("#paleta-inp").onkeydown = e => {
    if (e.key === "ArrowDown") { e.preventDefault(); mover(1); } else if (e.key === "ArrowUp") { e.preventDefault(); mover(-1); }
    else if (e.key === "Enter") { e.preventDefault(); executar(pSel); } else if (e.key === "Escape") fecharPaleta();
  };
  $("#paleta-fundo").onclick = e => { if (e.target.id === "paleta-fundo") fecharPaleta(); };
  document.addEventListener("keydown", e => {
    if (e.ctrlKey && e.key.toLowerCase() === "k") { e.preventDefault(); abrirPaleta(); }
    if (e.key === "F2") { e.preventDefault(); $("#bt-registrar-topo").click(); }
  });
}

window.addEventListener("pywebviewready", async () => {
  api = window.pywebview.api;
  await atualizarEstado();
  ligar();
  relogio(); setInterval(relogio, 1000);
  setInterval(() => { if (pagina === "inicio") carregarInicio(); }, 60000);
  await ir("inicio");
  const c = $("#carregando"); c.style.opacity = 0; setTimeout(() => c.remove(), 260);
});
