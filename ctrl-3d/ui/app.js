"use strict";
/* CTRL 3D • interface. Toda a conta e o banco ficam no Python (core.py); aqui só a tela. */

let api = null;     // ponte com o Python
let E = null;       // estado atual (config, rolos, canais, produtos…)
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const virgula = v => (Number.isInteger(v) ? String(v) : String(Math.round(v * 100) / 100).replace(".", ","));
const CMYK = ["#00AEEF", "#EC008C", "#FFD200", "#111111"];

const ICO = {
  x: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18"/></svg>',
  lixo: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 12.5A1.5 1.5 0 0 0 8.5 21h7a1.5 1.5 0 0 0 1.5-1.5L18 7M9 7V4.5A1.5 1.5 0 0 1 10.5 3h3A1.5 1.5 0 0 1 15 4.5V7"/></svg>',
  zap: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 11.5a8.4 8.4 0 0 1-12.4 7.4L3 21l2.1-5.5A8.4 8.4 0 1 1 21 11.5z"/></svg>',
  lapis: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20h4L19 9a2.8 2.8 0 0 0-4-4L4 16v4z"/><path d="M13.5 6.5l4 4"/></svg>',
  mais: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>',
  caixa: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8l-9-5-9 5 9 5 9-5z"/><path d="M3 8v8l9 5 9-5V8M12 13v8"/></svg>',
};

/* ------------------------------------------------------------------ avisos e janelas */
function toast(msg, tipo = "") {
  const t = document.createElement("div");
  t.className = "toast " + tipo;
  t.textContent = msg;
  $("#toasts").append(t);
  setTimeout(() => { t.style.transition = "opacity .3s"; t.style.opacity = 0; setTimeout(() => t.remove(), 300); }, 3400);
}

function modal({ titulo, texto = "", ok = "OK", cancelar = "Cancelar", input = null, perigo = false }) {
  return new Promise(res => {
    $("#m-tit").textContent = titulo;
    $("#m-txt").textContent = texto;
    const inp = $("#m-inp");
    inp.hidden = input === null;
    if (input !== null) inp.value = input;
    $("#m-sim").textContent = ok;
    $("#m-sim").className = "btn" + (perigo ? " perigo-forte" : "");
    $("#modal").classList.toggle("perigo", !!perigo);
    $("#m-nao").textContent = cancelar || "";
    $("#m-nao").hidden = cancelar === null;
    $("#modal-fundo").classList.add("on");
    // padrão do ERP: ação irreversível começa com o foco em Cancelar
    setTimeout(() => (input !== null ? (inp.focus(), inp.select()) : (perigo ? $("#m-nao") : $("#m-sim")).focus()), 30);
    const fim = v => {
      $("#modal-fundo").classList.remove("on");
      $("#m-sim").onclick = $("#m-nao").onclick = inp.onkeydown = null;
      res(v);
    };
    $("#m-sim").onclick = () => fim(input !== null ? inp.value : true);
    $("#m-nao").onclick = () => fim(input !== null ? null : false);
    inp.onkeydown = e => { if (e.key === "Enter") $("#m-sim").click(); if (e.key === "Escape") $("#m-nao").click(); };
  });
}

async function chama(nome, ...args) {
  try {
    return await api[nome](...args);
  } catch (e) {
    toast("Algo deu errado: " + (e && e.message ? e.message : e), "erro");
    throw e;
  }
}

function contraste(hex) {
  const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || "");
  if (!m) return "#fff";
  const [r, g, b] = m.slice(1).map(x => parseInt(x, 16));
  return 0.299 * r + 0.587 * g + 0.114 * b > 160 ? "#111" : "#fff";
}
const hexOk = h => /^#[0-9a-f]{6}$/i.test(h || "");

/* ------------------------------------------------------------------ navegação */
let pagina = "calc";
const CARREGAR = {
  calc: () => abrirCalc("venda"), pessoal: () => abrirCalc("pessoal"), catalogo: carregarCatalogo,
  pedidos: carregarPedidos, filamentos: carregarFilamentos, gastos: carregarGastos, resumo: carregarResumo,
  config: carregarConfig,
};

async function ir(p) {
  pagina = p;
  const secao = p === "pessoal" ? "calc" : p;          // o uso pessoal usa a mesma tela, em outro modo
  $$(".lateral [data-p]").forEach(b => b.classList.toggle("ativo", b.dataset.p === p));
  $$(".pagina").forEach(s => s.classList.toggle("ativa", s.id === "pg-" + secao));
  $("#main").scrollTop = 0;
  await atualizarEstado();
  await CARREGAR[p]();
}

async function atualizarEstado() {
  E = await chama("estado");
  const b = $("#badge-fila");
  b.hidden = !E.fila.n;
  b.textContent = E.fila.n;
  $("#chip-fila-txt").textContent = E.fila.n ? `Fila: ${E.fila.n} pedido(s) • ${E.fila.h}` : "Impressora livre";
  $("#chip-fila-ponto").className = "ponto " + (E.fila.n ? "warn" : "ok");
}

/* ------------------------------------------------------------------ paleta de comandos (Ctrl+K), igual ao ERP */
const ACOES = [
  { g: "Telas", t: "Calculadora — novo orçamento", a: () => { ir("calc"); limparCalc(); }, k: "Ctrl N" },
  { g: "Telas", t: "Catálogo de produtos", a: () => ir("catalogo") },
  { g: "Telas", t: "Uso pessoal", a: () => ir("pessoal") },
  { g: "Telas", t: "Pedidos e fila", a: () => ir("pedidos") },
  { g: "Telas", t: "Filamentos (estoque)", a: () => ir("filamentos") },
  { g: "Telas", t: "Gastos", a: () => ir("gastos") },
  { g: "Telas", t: "Resumo do mês", a: () => ir("resumo") },
  { g: "Telas", t: "Configurações", a: () => ir("config") },
  { g: "Ações", t: "Abrir arquivo do fatiador", a: async () => { await ir("calc"); App.aoImportar(await chama("escolher_arquivo")); } },
  { g: "Ações", t: "Fazer backup agora", a: async () => { if (await chama("backup")) toast("Backup salvo em Dados\\Backups!", "ok"); } },
  { g: "Ações", t: "Exportar resumo do mês para Excel", a: () => ir("resumo").then(() => $("#r-excel").click()) },
  { g: "Ações", t: "Manual de uso", a: () => chama("abrir_manual") },
];
let paletaSel = 0, paletaLista = [];
function abrirPaleta() {
  $("#paleta-fundo").classList.add("on");
  $("#paleta-inp").value = "";
  filtrarPaleta();
  setTimeout(() => $("#paleta-inp").focus(), 20);
}
function fecharPaleta() { $("#paleta-fundo").classList.remove("on"); }
function filtrarPaleta() {
  const q = $("#paleta-inp").value.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().trim();
  const produtos = (E && E.produtos || []).map(p => ({ g: "Produtos do catálogo", t: p, a: async () => { await ir("calc"); carregarProduto(p); } }));
  paletaLista = ACOES.concat(produtos).filter(x => !q || x.t.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().includes(q)).slice(0, 40);
  paletaSel = 0;
  let grupo = "";
  $("#paleta-itens").innerHTML = paletaLista.map((x, i) => {
    const cab = x.g !== grupo ? `<div class="grupo">${esc(grupo = x.g)}</div>` : "";
    return cab + `<div class="it ${i === 0 ? "sel" : ""}" data-i="${i}"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>${esc(x.t)}${x.k ? `<small>${x.k}</small>` : ""}</div>`;
  }).join("") || `<div class="vazio-tab">Nada encontrado.</div>`;
  $$("#paleta-itens .it").forEach(el => (el.onclick = () => executarPaleta(Number(el.dataset.i))));
}
function executarPaleta(i) { const x = paletaLista[i]; fecharPaleta(); if (x) x.a(); }
function moverPaleta(d) {
  if (!paletaLista.length) return;
  paletaSel = (paletaSel + d + paletaLista.length) % paletaLista.length;
  $$("#paleta-itens .it").forEach(el => el.classList.toggle("sel", Number(el.dataset.i) === paletaSel));
  const s = $(`#paleta-itens .it[data-i="${paletaSel}"]`); if (s) s.scrollIntoView({ block: "nearest" });
}

function opcoes(sel, lista, manter = true) {
  const atual = sel.value;
  sel.innerHTML = lista.map(o => typeof o === "string"
    ? `<option>${esc(o)}</option>` : `<option value="${esc(o.v)}">${esc(o.txt)}</option>`).join("");
  if (manter && [...sel.options].some(o => o.value === atual)) sel.value = atual;
}

/* ================================================================== CALCULADORA */
const calc = { cores: [], img: null };
let tCalc = null;
const agendar = () => { clearTimeout(tCalc); tCalc = setTimeout(recalcular, 110); };

function rolo(id) { return E.rolos.find(r => r.id === id); }

function corDaLinha(c, i) {
  if (hexOk(c.hex)) return c.hex;
  const r = rolo(c.rolo);
  if (r && hexOk(r.cor)) return r.cor;
  return CMYK[i % 4];
}

function fecharPickers(exceto) { $$(".picker.aberto").forEach(p => p !== exceto && p.classList.remove("aberto")); }
document.addEventListener("click", e => { if (!e.target.closest(".picker")) fecharPickers(); });

function picker(c) {
  const w = document.createElement("div");
  w.className = "picker";
  const r = rolo(c.rolo);
  const dot = r ? (hexOk(r.cor) ? `<span class="dot" style="--c:${r.cor}"></span>` : `<span class="dot listrado"></span>`)
                : `<span class="dot listrado"></span>`;
  w.innerHTML = `<button type="button">${dot}<span class="nome">${esc(r ? `${r.nome} • ${r.tipo}` : "Filamento padrão")}</span>
      <span class="det">${esc(r ? r.restante : `R$ ${E.padrao_kg}/kg`)}</span></button><div class="lista"></div>`;
  const lista = $(".lista", w);
  const ops = [{ id: null, nome: "Filamento padrão", det: `R$ ${E.padrao_kg}/kg`, cor: "" }]
    .concat(E.rolos.map(x => ({ id: x.id, nome: `${x.nome} • ${x.tipo}${x.marca ? " " + x.marca : ""}`, det: `${x.rkg}/kg • ${x.restante}`, cor: x.cor, baixo: x.baixo })));
  lista.innerHTML = ops.map(o => `<div class="op ${o.id === (c.rolo ?? null) ? "sel" : ""}" data-id="${o.id ?? ""}">
      ${hexOk(o.cor) ? `<span class="dot" style="--c:${o.cor}"></span>` : `<span class="dot listrado"></span>`}
      <span>${esc(o.nome)}</span><span class="det" ${o.baixo ? 'style="color:var(--verm)"' : ""}>${esc(o.det)}</span></div>`).join("")
    + (E.rolos.length ? "" : `<div class="vazio">Nenhum rolo cadastrado ainda.</div>`)
    + `<div class="rodape-lista"><button class="link" type="button">+ cadastrar rolo</button></div>`;
  $("button", w).onclick = () => { fecharPickers(w); w.classList.toggle("aberto"); };
  $$(".op", lista).forEach(op => op.onclick = () => {
    c.rolo = op.dataset.id ? Number(op.dataset.id) : null;
    w.classList.remove("aberto");
    renderCores();
    agendar();
  });
  $(".rodape-lista .link", lista).onclick = () => ir("filamentos");
  return w;
}

function renderCores() {
  const box = $("#cores");
  box.innerHTML = "";
  calc.cores.forEach((c, i) => {
    const lin = document.createElement("div");
    lin.className = "cor-linha";
    const cor = corDaLinha(c, i);
    lin.innerHTML = `<span class="n" style="--c:${cor}; --t:${contraste(cor)}">${i + 1}</span>`;
    lin.append(picker(c));
    const g = document.createElement("input");
    g.className = "inp";
    g.placeholder = "gramas";
    g.value = c.g || "";
    g.oninput = () => { c.g = g.value; agendar(); };
    lin.append(g);
    lin.insertAdjacentHTML("beforeend", `<span class="uni">g</span>`);
    const del = document.createElement("button");
    del.className = "icone del";
    del.title = "Remover cor";
    del.innerHTML = ICO.x;
    del.onclick = () => {
      if (calc.cores.length > 1) calc.cores.splice(i, 1); else Object.assign(c, { g: "", hex: "", rolo: null });
      renderCores(); agendar();
    };
    lin.append(del);
    box.append(lin);
  });
}

function formCalc(comFoto = false) {
  const f = {};
  $$("#pg-calc [data-k]").forEach(i => (f[i.dataset.k] = i.value));
  f.cores = calc.cores.map(c => ({ rolo: c.rolo ?? null, g: c.g || "", hex: c.hex || "" }));
  if (comFoto) f.img = calc.img;
  return f;
}
const campoCalc = k => $(`#pg-calc [data-k="${k}"]`);

function mostrarFoto(img) {
  calc.img = img || null;
  $("#foto").hidden = !img;
  if (img) $("#foto-img").src = img;
}

async function recalcular() {
  const r = await chama("calcular", formCalc());
  $("#h-tempo").textContent = r.hero.tempo;
  $("#h-pronto").textContent = "fica pronto " + r.hero.pronto;
  $("#h-custo").textContent = r.hero.custo_un;
  $("#h-custo-t").textContent = "pedido: " + r.hero.custo;
  $("#h-preco").textContent = r.hero.preco_un;
  $("#h-total").textContent = "total: " + r.hero.total;
  $("#frase").innerHTML = r.frase.map(l => `<div>${l}</div>`).join("");
  const dm = r.dica_mesa;
  $("#dica-mesa").hidden = !dm;
  if (dm) {
    $("#dm-tit").textContent = dm.titulo;
    $("#dm-txt").textContent = dm.texto;
    $("#dm-usar").textContent = `Usar ${dm.cap}`;
    $("#dm-usar").onclick = usarCapacidade;
  }
  const sec = (t, cor, linhas, extra = "") => `<h3><i style="--c:${cor}"></i>${t}</h3>` + linhas.map(l =>
    `<div class="ln ${l[2] ? "forte" : ""} ${l[3] || ""}"><span>${esc(l[0])}</span><span>${esc(l[1])}</span></div>`).join("") + extra;
  const alertas = r.alertas.map(a => `<div class="alerta">${esc(a)}</div>`).join("");
  const pessoal = modo === "pessoal";
  const rolos = r.rolos.length ? `<div class="rolos">${r.rolos.map(esc).join(" • ")}</div>` : "";
  $("#det").innerHTML = alertas
    + (pessoal ? sec("Quanto vai gastar", "var(--verde)", r.pessoal.linhas) : "")
    + sec("Prazo", "var(--cy)", r.prazo.filter(l => !pessoal || !/fila|fica pronto/i.test(l[0])))
    + sec("Mesa", "var(--ye)", r.mesa)
    + sec("Material", "var(--mg)", r.material, rolos)
    + (pessoal ? "" : sec("Custos do pedido", "var(--ye)", r.custos) + sec("Venda e lucro", "var(--bk)", r.venda));
  // uso pessoal: quadros de gasto
  $("#p-tempo").textContent = r.hero.tempo;
  $("#p-pronto").textContent = "fica pronto " + r.hero.pronto;
  $("#p-total").textContent = r.pessoal.total;
  $("#p-un").textContent = r.pessoal.un + " por peça";
  $("#p-fil").textContent = r.pessoal.filamento;
  // tamanho para impressão (selo na foto e linha embaixo do arquivo)
  $("#selo-tam").hidden = !r.tamanho;
  $("#selo-tam").innerHTML = r.tamanho ? `<small>TAMANHO</small>${esc(r.tamanho)}` : "";
  $("#tam-linha").hidden = !r.tamanho || !$("#foto").hidden;
  $("#tam-linha span").textContent = r.tamanho;
  // etapa 2: o que interessa ao escolher a quantidade
  $("#det-mesa").innerHTML = alertas + sec("Mesa da impressora", "var(--ye)", r.mesa)
    + sec("Tempo", "var(--cy)", r.prazo.filter(l => /Impressões|Tempo total|fica pronto/i.test(l[0])));
  // barrinha ao vivo (etapas 1 a 3)
  $("#ao-vivo").innerHTML = `<span>⏱ <b>${r.hero.tempo}</b> no total</span><span>Pronto <b>${r.hero.pronto}</b></span>`
    + (pessoal ? `<span>Gasto <b>${r.pessoal.total}</b></span><span>Filamento <b>${r.pessoal.filamento}</b></span>`
               : `<span>Custo <b>${r.hero.custo_un}</b>/un.</span><span>Preço <b>${r.hero.preco_un}</b>/un.</span>`);
  // cabeçalho do resumo
  const qtd = campoCalc("qtd").value || 1, cli = campoCalc("cliente").value.trim();
  $("#r-titulo").textContent = `${qtd}× ${campoCalc("peca").value.trim() || "Peça 3D"}`;
  $("#r-sub").textContent = pessoal ? (r.tamanho ? "Tamanho: " + r.tamanho : "Uso pessoal")
    : [cli, $("#sel-canal").value, r.tamanho].filter(Boolean).join(" • ");
  $("#r-foto").style.backgroundImage = calc.img ? `url("${calc.img}")` : "";
}

/* ------------------------------------------------------------------ etapas */
let etapa = 1;
function irEtapa(n, sentido = 1) {
  if (n === 3 && modo === "pessoal") n = sentido > 0 ? 4 : 2;     // uso pessoal não tem etapa de preço
  etapa = Math.min(4, Math.max(1, n));
  $$("#pg-calc .etapa").forEach(d => d.classList.toggle("ativa", Number(d.dataset.e) === etapa));
  $$("#passos button").forEach(b => {
    const e = Number(b.dataset.e);
    b.classList.toggle("ativo", e === etapa);
    b.classList.toggle("feito", e < etapa);
  });
  $$("#passos > i").forEach((t, i) => t.classList.toggle("feito", i + 1 < etapa));
  $("#nav-voltar").style.visibility = etapa === 1 ? "hidden" : "visible";
  $("#nav-proximo").style.display = etapa === 4 ? "none" : "";
  $("#ao-vivo").style.visibility = etapa === 4 ? "hidden" : "visible";
  if (etapa === 4 && modo === "pessoal") carregarListaPessoal();
  const ultimo = etapa === 3 || (etapa === 2 && modo === "pessoal");
  $("#nav-proximo").innerHTML = (ultimo ? (modo === "pessoal" ? "Ver gastos" : "Ver resumo") : "Próximo") +
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"/></svg>';
  $("#main").scrollTop = 0;
  const foco = { 1: null, 2: "qtd", 3: "margem" }[etapa];
  if (foco) setTimeout(() => { campoCalc(foco).focus(); campoCalc(foco).select(); }, 60);
}

function proximaEtapa() {
  if (etapa === 1) {
    const t = numero(campoCalc("th").value) * 60 + numero(campoCalc("tm").value);
    if (t <= 0) {
      toast("Solte o arquivo do fatiador ou coloque o tempo de impressão.", "aviso");
      campoCalc("th").focus();
      return;
    }
  }
  irEtapa(etapa + 1);
}

const numero = v => {          // mesmo jeito do Python: "1.234,5" e "1.5" funcionam
  const s = String(v ?? "").trim();
  return parseFloat(s.includes(",") ? s.replace(/\./g, "").replace(",", ".") : s) || 0;
};
/* Encher a mesa: volta "peças por impressão" para automático (o máximo que cabe). */
function usarCapacidade() {
  campoCalc("por_mesa").value = "";
  agendar();
  toast("Usando o máximo que cabe na mesa.", "ok");
}
/* A peça não pode ser maior que a mesa da impressora. */
function limitarTamanho(k, max) {
  const i = campoCalc(k);
  if (numero(i.value) > max) {
    i.value = virgula(max);
    toast(`O máximo da impressora é ${virgula(E.cfg.mesa_x)} × ${virgula(E.cfg.mesa_y)} × ${virgula(E.cfg.mesa_z)} mm.`, "aviso");
    agendar();
  }
}

let modo = "venda";
async function abrirCalc(m = "venda") {
  const trocou = m !== modo;
  modo = m;
  document.body.classList.toggle("modo-pessoal", modo === "pessoal");
  $("#calc-titulo").textContent = modo === "pessoal" ? "Impressão pessoal" : "Novo orçamento";
  $("#calc-sub").textContent = modo === "pessoal"
    ? "Para você mesmo: veja quanto vai gastar de filamento, luz e máquina — sem preço nem lucro."
    : "Siga as etapas — no fim aparece o resumo completo com preço, prazo e lucro.";
  if (trocou && etapa === 3 && modo === "pessoal") irEtapa(2);
  $("#dica-tam").textContent = `Vem do arquivo. Máximo da impressora: ${virgula(E.cfg.mesa_x)} × ${virgula(E.cfg.mesa_y)} × ${virgula(E.cfg.mesa_z)} mm.`;
  opcoes($("#sel-canal"), E.canais.map(c => c.nome));
  opcoes($("#sel-produto"), [{ v: "", txt: "— produto novo —" }].concat(E.produtos.map(p => ({ v: p, txt: p }))));
  renderCores();
  agendar();
  if (modo === "pessoal" && etapa === 4) carregarListaPessoal();
}

async function carregarListaPessoal() {
  const ym = new Date().toISOString().slice(0, 7);
  const r = await chama("pessoal", ym);
  $("#pl-total").textContent = r.n ? `${r.n} impressão(ões) • ${r.total} • ${r.gramas} • ${r.horas}` : "";
  const t = $("#tab-pessoal");
  if (!r.itens.length) {
    t.innerHTML = `<tr><td class="vazio-tab">Nenhuma impressão pessoal registrada neste mês.</td></tr>`;
    return;
  }
  t.innerHTML = `<thead><tr><th>Data</th><th>Peça</th><th class="cen">Qtd</th><th class="num">Filamento</th><th class="cen">Tempo</th><th class="num">Gasto</th><th></th></tr></thead><tbody>` +
    r.itens.map(i => `<tr><td class="mut">${i.data}</td><td class="forte">${esc(i.peca)}</td><td class="cen">${i.qtd}</td>
      <td class="num">${i.gramas}</td><td class="cen">${i.tempo}</td><td class="num forte">${i.gasto}</td>
      <td class="fim"><button class="icone del" data-del="${i.id}" title="Apagar (devolve o filamento)">${ICO.lixo}</button></td></tr>`).join("") + "</tbody>";
  $$("[data-del]", t).forEach(b => b.onclick = async () => {
    if (await modal({ titulo: "Apagar esse registro?", texto: "O filamento volta para o estoque.", ok: "Apagar", perigo: true })) {
      await chama("excluir_pessoal", Number(b.dataset.del));
      await atualizarEstado(); carregarListaPessoal();
    }
  });
}

function limparCalc() {
  const pad = { peca: "", cliente: "", qtd: 1, th: 0, tm: 0, ppm: 1, peca_l: "", peca_p: "", peca_a: "", por_mesa: "", purga: 0, preparo: 10, acab: 0,
                emb: virgula(E.cfg.embalagem), extras: 0, margem: virgula(E.cfg.margem), preco_manual: "" };
  for (const [k, v] of Object.entries(pad)) campoCalc(k).value = v;
  calc.cores = [{ rolo: null, g: "", hex: "" }];
  mostrarFoto(null);
  $("#importado").classList.remove("on");
  $("#sel-produto").value = "";
  renderCores();
  agendar();
  irEtapa(1);
}

async function carregarProduto(nome) {
  const p = await chama("produto", nome);
  if (!p) return;
  for (const k of ["th", "tm", "ppm", "peca_l", "peca_p", "peca_a", "por_mesa", "purga", "preparo", "acab", "emb", "extras", "margem"])
    if (campoCalc(k)) campoCalc(k).value = p[k] ?? "";
  campoCalc("peca").value = nome;
  campoCalc("preco_manual").value = "";
  calc.cores = (p.cores && p.cores.length ? p.cores : [{ rolo: null, g: "" }]).map(c => ({ rolo: c.rolo ?? null, g: c.g || "", hex: c.hex || "" }));
  mostrarFoto(p.img);
  $("#importado").classList.remove("on");
  $("#sel-produto").value = nome;
  renderCores();
  agendar();
  irEtapa(2);                      // peça pronta: vai direto para a quantidade
  toast(`"${nome}" carregado — agora é só a quantidade.`, "ok");
}

/* chamado pelo Python quando um arquivo é solto na janela */
window.App = {
  async aoImportar(res) {
    if (!res) return;
    if (res.erro) { await modal({ titulo: "Não deu para ler esse arquivo", texto: res.erro, cancelar: null }); return; }
    if (pagina !== "calc" && pagina !== "pessoal") await ir("calc");
    campoCalc("th").value = res.th;
    campoCalc("tm").value = res.tm;
    campoCalc("ppm").value = res.ppm || 1;
    campoCalc("peca_l").value = res.peca_l ?? "";
    campoCalc("peca_p").value = res.peca_p ?? "";
    campoCalc("peca_a").value = res.peca_a ?? "";
    campoCalc("por_mesa").value = "";
    if (res.aviso_mesa) toast(res.aviso_mesa, "aviso");
    if (res.cores.length) {
      campoCalc("purga").value = 0;
      calc.cores = res.cores.map(c => ({ rolo: c.rolo ?? null, g: c.g, hex: c.hex }));
    }
    campoCalc("peca").value = res.nome;
    $("#sel-produto").value = "";
    irEtapa(1);
    mostrarFoto(res.img);
    $("#importado span").textContent = res.resumo + " — confira o rolo de cada cor.";
    $("#importado").classList.add("on");
    renderCores();
    agendar();
    toast("Arquivo lido!", "ok");
  },
};

async function salvarCatalogo() {
  const nome = campoCalc("peca").value.trim();
  const f = formCalc(true);
  const dados = {};
  for (const k of ["th", "tm", "ppm", "peca_l", "peca_p", "peca_a", "por_mesa", "purga", "preparo", "acab", "emb", "extras", "margem", "cores", "img"]) dados[k] = f[k];
  let r = await chama("salvar_produto", nome, dados, false);
  if (r.existe) {
    if (!(await modal({ titulo: "Já está no catálogo", texto: `"${nome}" já existe. Atualizar com estes dados?`, ok: "Atualizar" }))) return;
    r = await chama("salvar_produto", nome, dados, true);
  }
  if (r.erro) return toast(r.erro, "aviso");
  await atualizarEstado();
  await abrirCalc();
  $("#sel-produto").value = nome;
  toast(`"${nome}" salvo no catálogo!`, "ok");
}

/* ================================================================== CATÁLOGO */
async function carregarCatalogo() {
  opcoes($("#cat-canal"), E.canais.map(c => c.nome));
  const linhas = await chama("catalogo", $("#cat-canal").value);
  const t = $("#tab-cat");
  if (!linhas.length) {
    t.innerHTML = `<tr><td class="vazio-tab">${ICO.caixa}Nenhum produto ainda.<br>Monte uma peça na Calculadora e clique em <b>Salvar no catálogo</b>.</td></tr>`;
    return;
  }
  t.innerHTML = `<thead><tr><th>Produto</th><th class="cen">Tempo/peça</th><th class="cen">Cabem/impr.</th><th class="num">Filam./un.</th>
      <th class="num">Custo 1 un.</th><th class="num">1 un.</th><th class="num">10 un.</th><th class="num">50 un.</th><th class="num">100 un.</th>
      <th class="cen">Prazo 50 un.</th><th></th></tr></thead><tbody>` +
    linhas.map(l => `<tr data-nome="${esc(l.nome)}">
      <td class="forte"><div class="junto" style="gap:0">${l.img ? `<img class="mini" src="${l.img}">` : `<span class="mini vazia">◇</span>`}<div><div style="white-space:nowrap">${esc(l.nome)}</div><span class="cores-mini" style="margin-top:4px">${l.cores.filter(hexOk).map(c => `<span class="dot" style="--c:${c}"></span>`).join("")}</span></div></div></td>
      <td class="cen">${l.tempo}</td><td class="cen">${l.ppm}</td><td class="num">${l.g}</td><td class="num mut">${l.custo}</td>
      ${l.precos.map((p, i) => `<td class="num ${i === 3 ? "forte" : ""}">${p}</td>`).join("")}
      <td class="cen mut">${l.prazo}</td>
      <td class="fim"><button class="btn cy peq" data-usar>Usar</button> <button class="icone del" data-del="${l.id}" title="Excluir">${ICO.lixo}</button></td></tr>`).join("") + "</tbody>";
  $$("tr[data-nome]", t).forEach(tr => {
    const usar = async () => { await ir("calc"); await carregarProduto(tr.dataset.nome); };
    tr.ondblclick = usar;
    $("[data-usar]", tr).onclick = usar;
    $("[data-del]", tr).onclick = async () => {
      if (await modal({ titulo: "Excluir produto?", texto: `"${tr.dataset.nome}" sai do catálogo.`, ok: "Excluir", perigo: true })) {
        await chama("excluir_produto", Number($("[data-del]", tr).dataset.del));
        await atualizarEstado(); carregarCatalogo();
      }
    };
  });
}

/* ================================================================== PEDIDOS */
const stClasse = s => "st-" + s.split(" ")[0];

async function carregarPedidos() {
  const r = await chama("pedidos", $("#ped-filtro").value, $("#ped-busca").value);
  $("#fila").innerHTML = r.fila
    ? `<span class="rot">FILA DA<br>IMPRESSORA</span>
       <div class="it"><small>Pedidos</small><b>${r.fila.n}</b></div>
       <div class="it"><small>Tempo de impressão</small><b>${r.fila.h}</b></div>
       <div class="it"><small>Dias</small><b>≈ ${r.fila.dias}</b></div>
       <div class="livre">Livre em ${r.fila.livre}</div>`
    : `<span class="rot">FILA DA<br>IMPRESSORA</span><div class="it"><b>Vazia</b><small>impressora livre para novos pedidos</small></div>`;
  $("#ped-total").textContent = r.total;
  const t = $("#tab-ped");
  if (!r.linhas.length) {
    t.innerHTML = `<tr><td class="vazio-tab">${ICO.caixa}Nenhum pedido aqui.<br>Salve um orçamento pela Calculadora.</td></tr>`;
    return;
  }
  t.innerHTML = `<thead><tr><th>#</th><th>Data</th><th>Cliente</th><th>Peça</th><th class="cen">Qtd</th><th class="cen">Tempo</th>
      <th class="num">Valor</th><th class="num">Lucro</th><th>Status</th><th>Fica pronto</th><th></th></tr></thead><tbody>` +
    r.linhas.map(l => `<tr data-id="${l.id}"><td class="mut">${l.id}</td><td class="mut">${l.data}</td><td class="forte">${esc(l.cliente)}</td>
      <td>${esc(l.peca)}</td><td class="cen">${l.qtd}</td><td class="cen">${l.tempo}</td><td class="num forte">${l.preco}</td>
      <td class="num ${l.neg ? "neg" : ""}">${l.lucro}</td>
      <td><select class="pill ${stClasse(l.status)}">${E.status.map(s => `<option ${s === l.status ? "selected" : ""}>${s}</option>`).join("")}</select></td>
      <td class="mut">${esc(l.pronto)}</td>
      <td class="fim"><button class="icone" data-zap title="Copiar para WhatsApp">${ICO.zap}</button><button class="icone del" data-del title="Excluir">${ICO.lixo}</button></td></tr>`).join("") + "</tbody>";
  $$("tr[data-id]", t).forEach(tr => {
    const id = Number(tr.dataset.id);
    $("select", tr).onchange = async e => {
      const msgs = await chama("mudar_status", [id], e.target.value);
      msgs.forEach(m => toast(m, "ok"));
      await atualizarEstado(); carregarPedidos();
    };
    $("[data-zap]", tr).onclick = async () => { await chama("copiar_pedido", id); toast("Orçamento copiado! Cole no WhatsApp (Ctrl+V).", "ok"); };
    $("[data-del]", tr).onclick = async () => {
      if (await modal({ titulo: `Excluir pedido #${id}?`, texto: "Não dá para desfazer.", ok: "Excluir", perigo: true })) {
        await chama("excluir_pedido", id);
        await atualizarEstado(); carregarPedidos();
      }
    };
  });
}

/* ================================================================== FILAMENTOS */
async function carregarFilamentos() {
  const r = await chama("filamentos");
  $("#fil-total").textContent = r.total;
  const t = $("#tab-fil");
  if (!r.linhas.length) {
    t.innerHTML = `<tr><td class="vazio-tab">${ICO.caixa}Nenhum rolo cadastrado.<br>Cadastre acima — com a cor, o programa acerta o rolo sozinho ao ler o arquivo.</td></tr>`;
    return;
  }
  t.innerHTML = `<thead><tr><th>Cor</th><th>Nome</th><th>Marca</th><th>Tipo</th><th class="num">R$/kg</th><th>Restante</th><th>Situação</th><th></th></tr></thead><tbody>` +
    r.linhas.map(l => `<tr data-id="${l.id}" data-nome="${esc(l.nome)}">
      <td><input type="color" class="cor" value="${hexOk(l.cor) ? l.cor : "#cccccc"}" title="Mudar a cor"></td>
      <td class="forte">${esc(l.nome)}</td><td class="mut">${esc(l.marca)}</td><td>${esc(l.tipo)}</td><td class="num">${l.rkg}</td>
      <td><span class="barra ${l.baixo ? "baixo" : ""}"><i style="width:${l.pct}%"></i></span> <span class="mut" style="margin-left:8px">${l.restante}</span></td>
      <td><span class="tag ${l.baixo ? "baixo" : "ok"}">${l.acabou ? "ACABOU" : l.baixo ? "ACABANDO" : "OK"}</span></td>
      <td class="fim"><button class="icone" data-aj title="Ajustar quanto sobrou">${ICO.lapis}</button><button class="icone" data-rep title="Repor +1 rolo">${ICO.mais}</button><button class="icone del" data-del title="Excluir">${ICO.lixo}</button></td></tr>`).join("") + "</tbody>";
  $$("tr[data-id]", t).forEach(tr => {
    const id = Number(tr.dataset.id), nome = tr.dataset.nome;
    $("input[type=color]", tr).onchange = async e => { await chama("cor_filamento", id, e.target.value); toast("Cor atualizada", "ok"); };
    $("[data-aj]", tr).onclick = async () => {
      const v = await modal({ titulo: `Quanto sobrou de ${nome}?`, texto: "Pese o rolo e desconte o peso do carretel vazio (em gramas).", input: "", ok: "Salvar" });
      if (v !== null && v.trim()) { await chama("ajustar_filamento", id, v); carregarFilamentos(); }
    };
    $("[data-rep]", tr).onclick = async () => {
      const lancar = await modal({ titulo: `Repor 1 rolo de ${nome}?`, texto: "Lançar o valor da compra em Gastos também?", ok: "Repor e lançar gasto", cancelar: "Só repor" });
      toast(await chama("repor_filamento", id, lancar), "ok");
      carregarFilamentos();
    };
    $("[data-del]", tr).onclick = async () => {
      if (await modal({ titulo: `Excluir ${nome}?`, texto: "O rolo sai do estoque.", ok: "Excluir", perigo: true })) {
        await chama("excluir_filamento", id); carregarFilamentos();
      }
    };
  });
}

async function addFilamento() {
  const d = { nome: $("#f-nome").value, marca: $("#f-marca").value, tipo: $("#f-tipo").value, preco: $("#f-preco").value,
              peso: $("#f-peso").value, qtd: $("#f-qtd").value, cor: $("#f-cor").value, lancar: $("#f-lancar").checked };
  const r = await chama("add_filamento", d);
  if (r.erro) return toast(r.erro, "aviso");
  ["#f-nome", "#f-marca", "#f-preco"].forEach(s => ($(s).value = ""));
  $("#f-qtd").value = 1;
  toast("Rolo adicionado!", "ok");
  carregarFilamentos();
}

/* ================================================================== GASTOS */
async function carregarGastos() {
  opcoes($("#g-mes"), E.meses);
  const r = await chama("gastos", $("#g-mes").value);
  $("#g-total").textContent = "Total do mês: " + r.total;
  const t = $("#tab-gas");
  if (!r.linhas.length) { t.innerHTML = `<tr><td class="vazio-tab">${ICO.caixa}Nenhum gasto neste mês.</td></tr>`; return; }
  t.innerHTML = `<thead><tr><th>Data</th><th>Categoria</th><th>Descrição</th><th class="num">Valor</th><th></th></tr></thead><tbody>` +
    r.linhas.map(l => `<tr><td class="mut">${l.data}</td><td><span class="tag neutral">${esc(l.cat)}</span></td>
      <td>${esc(l.desc)}</td><td class="num forte">${l.valor}</td><td class="fim"><button class="icone del" data-del="${l.id}">${ICO.lixo}</button></td></tr>`).join("") + "</tbody>";
  $$("[data-del]", t).forEach(b => b.onclick = async () => {
    if (await modal({ titulo: "Excluir gasto?", ok: "Excluir", perigo: true })) { await chama("excluir_gasto", Number(b.dataset.del)); carregarGastos(); }
  });
}

async function addGasto() {
  const r = await chama("add_gasto", { data: $("#g-data").value, cat: $("#g-cat").value, desc: $("#g-desc").value, valor: $("#g-valor").value });
  if (r.erro) return toast(r.erro, "aviso");
  $("#g-desc").value = $("#g-valor").value = "";
  await atualizarEstado();
  opcoes($("#g-mes"), E.meses);
  $("#g-mes").value = r.ym;
  toast("Gasto lançado!", "ok");
  carregarGastos();
}

/* ================================================================== RESUMO */
async function carregarResumo() {
  opcoes($("#r-mes"), E.meses);
  const r = await chama("resumo", $("#r-mes").value);
  const cls = ["cy", "verde", "mg", "bk", "cy", "mg", "ye", "bk"];
  $("#r-tiles").innerHTML = r.tiles.map((t, i) => `<div class="tile ${cls[i]}"><small>${t[0]}</small><div class="v${String(t[1]).startsWith("-") ? " neg" : ""}">${t[1]}</div><div class="s">${t[2]}</div></div>`).join("");
  const lista = (l, vazio) => l.length ? l.map(x => `<div class="ln"><span>${esc(x[0])}</span><b>${esc(x[1])}</b></div>`).join("") : `<div class="nota">${vazio}</div>`;
  $("#r-pessoal").hidden = !r.pessoal;
  $("#r-pessoal").innerHTML = r.pessoal ? `<b>Uso pessoal no mês:</b> ${esc(r.pessoal)}` : "";
  $("#r-cats").innerHTML = lista(r.cats, "Nenhum gasto lançado.");
  $("#r-top").innerHTML = lista(r.top, "Nenhum pedido no mês.");
}

/* ================================================================== CONFIGURAÇÕES */
async function carregarConfig() {
  $("#c-campos").innerHTML = E.cfg_def.map(c => `<div class="campo"><label>${esc(c.rot)}</label>
      <div class="junto"><input class="inp m" data-cfg="${c.k}" value="${virgula(E.cfg[c.k])}"><span class="uni">${esc(c.uni)}</span></div>
      ${c.dica ? `<div class="dica">${esc(c.dica)}</div>` : ""}</div>`).join("");
  $("#tab-canais").innerHTML = `<thead><tr><th>Canal</th><th>Taxas</th><th></th></tr></thead><tbody>` +
    E.canais.map(c => `<tr><td class="forte">${esc(c.nome)}</td><td>${esc(c.txt)}</td>
      <td class="fim"><button class="icone" data-ed="${c.id}" title="Editar">${ICO.lapis}</button><button class="icone del" data-del="${c.id}" title="Remover">${ICO.lixo}</button></td></tr>`).join("") + "</tbody>";
  $$("#tab-canais [data-ed]").forEach(b => b.onclick = () => {
    const c = E.canais.find(x => x.id === Number(b.dataset.ed));
    $("#cn-nome").value = c.nome; $("#cn-pct").value = virgula(c.pct); $("#cn-fixa").value = virgula(c.fixa);
    $("#cn-nome").focus();
  });
  $$("#tab-canais [data-del]").forEach(b => b.onclick = async () => {
    if (await modal({ titulo: "Remover canal?", ok: "Remover", perigo: true })) { await chama("rem_canal", Number(b.dataset.del)); await atualizarEstado(); carregarConfig(); }
  });
  $("#c-caminho").textContent = "Tudo fica salvo em: " + E.caminho;
}

/* Arquivo solto na janela.
   1) pede ao WebView2 o caminho completo do arquivo e o Python lê direto do disco;
   2) se o caminho não vier, lê o conteúdo aqui e manda para o Python (plano B). */
const EXT_OK = /\.(gcode|gco|bgcode|3mf|stl|obj|step|stp)$/i;
async function soltarArquivo(dt) {
  const arq = dt && dt.files && dt.files[0];
  if (!arq) return;
  if (!EXT_OK.test(arq.name)) {
    toast("Esse tipo de arquivo não serve. Use .gcode, .bgcode ou .gcode.3mf.", "aviso");
    return;
  }
  toast("Lendo o arquivo…");
  let res = null;
  try {
    if (window.chrome && chrome.webview && chrome.webview.postMessageWithAdditionalObjects) {
      chrome.webview.postMessageWithAdditionalObjects("FilesDropped", dt.files);
      res = await chama("arquivo_solto", arq.name);
    }
  } catch (e) { res = null; }
  if (!res) {
    if (arq.size > 80 * 1024 * 1024) {
      toast("Arquivo muito grande para arrastar. Clique na caixa rosa e escolha o arquivo.", "aviso");
      return;
    }
    const b64 = await new Promise((ok, erro) => {
      const fr = new FileReader();
      fr.onload = () => ok(String(fr.result).split(",", 2)[1] || "");
      fr.onerror = () => erro(fr.error);
      fr.readAsDataURL(arq);
    });
    res = await chama("arquivo_conteudo", arq.name, b64);
  }
  App.aoImportar(res);
}

/* ================================================================== início */
function ligarEventos() {
  $$(".lateral [data-p]").forEach(b => (b.onclick = () => ir(b.dataset.p)));
  $("#bt-comando").onclick = abrirPaleta;
  $("#bt-novo").onclick = async () => { await ir("calc"); limparCalc(); };
  $("#chip-fila").onclick = () => ir("pedidos");
  $("#paleta-inp").oninput = filtrarPaleta;
  $("#paleta-inp").onkeydown = e => {
    if (e.key === "ArrowDown") { e.preventDefault(); moverPaleta(1); }
    else if (e.key === "ArrowUp") { e.preventDefault(); moverPaleta(-1); }
    else if (e.key === "Enter") { e.preventDefault(); executarPaleta(paletaSel); }
    else if (e.key === "Escape") fecharPaleta();
  };
  $("#paleta-fundo").onclick = e => { if (e.target.id === "paleta-fundo") fecharPaleta(); };
  document.addEventListener("keydown", e => {
    if (e.ctrlKey && e.key.toLowerCase() === "k") { e.preventDefault(); abrirPaleta(); }
    if (e.ctrlKey && e.key.toLowerCase() === "n") { e.preventDefault(); $("#bt-novo").click(); }
  });
  $$("[data-ir]").forEach(b => (b.onclick = () => ir(b.dataset.ir)));
  $$("#pg-calc [data-k]").forEach(i => i.addEventListener("input", agendar));
  $("#sel-canal").addEventListener("change", agendar);
  $("#nav-proximo").onclick = proximaEtapa;
  $("#nav-voltar").onclick = () => irEtapa(etapa - 1, -1);
  $$("#passos button").forEach(b => (b.onclick = () => (Number(b.dataset.e) > etapa && etapa === 1 ? proximaEtapa() : irEtapa(Number(b.dataset.e)))));
  // Enter num campo da etapa = próximo
  $$("#pg-calc .etapa input").forEach(i => i.addEventListener("keydown", e => { if (e.key === "Enter") proximaEtapa(); }));
  campoCalc("peca_l").addEventListener("change", () => limitarTamanho("peca_l", Math.max(E.cfg.mesa_x, E.cfg.mesa_y)));
  campoCalc("peca_p").addEventListener("change", () => limitarTamanho("peca_p", Math.max(E.cfg.mesa_x, E.cfg.mesa_y)));
  campoCalc("peca_a").addEventListener("change", () => limitarTamanho("peca_a", E.cfg.mesa_z));
  $("#bt-registrar").onclick = async () => {
    const r = await chama("registrar_pessoal", formCalc());
    if (r.erro) return toast(r.erro, "aviso");
    toast(r.txt, "ok");
    await atualizarEstado();
    renderCores();
    carregarListaPessoal();
  };
  $("#sel-produto").onchange = e => { if (e.target.value) carregarProduto(e.target.value); };
  $("#add-cor").onclick = () => { calc.cores.push({ rolo: null, g: "", hex: "" }); renderCores(); agendar(); };
  $("#soltar").onclick = async () => App.aoImportar(await chama("escolher_arquivo"));
  $("#bt-limpar").onclick = limparCalc;
  $("#bt-catalogo").onclick = salvarCatalogo;
  $("#bt-salvar").onclick = async () => {
    const r = await chama("salvar_pedido", formCalc());
    if (r.erro) return toast(r.erro, "aviso");
    await atualizarEstado();
    toast("Salvo como Orçamento! Ao aprovar, ele entra na fila.", "ok");
  };
  $("#bt-zap").onclick = async () => { await chama("copiar_orcamento", formCalc()); toast("Orçamento copiado! Cole no WhatsApp (Ctrl+V).", "ok"); };

  $("#cat-canal").onchange = carregarCatalogo;
  $("#ped-filtro").onchange = carregarPedidos;
  let tb; $("#ped-busca").oninput = () => { clearTimeout(tb); tb = setTimeout(carregarPedidos, 200); };
  $("#f-add").onclick = addFilamento;
  $("#g-add").onclick = addGasto;
  $("#g-valor").onkeydown = e => e.key === "Enter" && addGasto();
  $("#g-mes").onchange = carregarGastos;
  $("#r-mes").onchange = carregarResumo;
  $("#r-excel").onclick = async () => { const r = await chama("exportar_excel", $("#r-mes").value); if (r && r.erro) toast(r.erro, "erro"); };
  $("#c-salvar").onclick = async () => {
    const d = {}; $$("[data-cfg]").forEach(i => (d[i.dataset.cfg] = i.value));
    await chama("salvar_config", d); await atualizarEstado(); toast("Configurações salvas!", "ok");
  };
  $("#cn-add").onclick = async () => {
    await chama("add_canal", $("#cn-nome").value, $("#cn-pct").value, $("#cn-fixa").value);
    ["#cn-nome", "#cn-pct", "#cn-fixa"].forEach(s => ($(s).value = ""));
    await atualizarEstado(); carregarConfig();
  };
  $("#c-pasta").onclick = () => chama("abrir_pasta");
  $("#c-backup").onclick = async () => { if (await chama("backup")) toast("Backup salvo em Dados\\Backups!", "ok"); };
  $("#bt-ajuda").onclick = async () => { if (!(await chama("abrir_manual"))) toast("Manual não encontrado na pasta Documentacao.", "aviso"); };

  // arrastar arquivo: aviso rosa (que nunca fica preso) + leitura do arquivo
  let tAviso = null;
  const aviso = on => { clearTimeout(tAviso); $("#arrastar").classList.toggle("on", on); };
  const temArquivo = e => [...((e.dataTransfer && e.dataTransfer.types) || [])].includes("Files");
  document.addEventListener("dragenter", e => { if (temArquivo(e)) { e.preventDefault(); aviso(true); } });
  document.addEventListener("dragover", e => {
    if (!temArquivo(e)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
    aviso(true);
    tAviso = setTimeout(() => aviso(false), 500);       // parou de mexer / saiu da janela: some
  });
  document.addEventListener("dragleave", e => { if (!e.relatedTarget) aviso(false); });
  document.addEventListener("drop", e => { e.preventDefault(); aviso(false); soltarArquivo(e.dataTransfer); });
  document.addEventListener("keydown", e => { if (e.key === "Escape") { fecharPickers(); aviso(false); } });
  $("#arrastar").addEventListener("click", () => aviso(false));
}

window.addEventListener("pywebviewready", async () => {
  api = window.pywebview.api;
  await atualizarEstado();
  opcoes($("#f-tipo"), E.tipos);
  opcoes($("#g-cat"), E.categorias);
  opcoes($("#ped-filtro"), ["Todos", "Na fila", "Em aberto"].concat(E.status));
  $("#g-data").value = E.hoje;
  ligarEventos();
  limparCalc();
  await ir("calc");
  const c = $("#carregando");
  c.style.opacity = 0;
  setTimeout(() => c.remove(), 260);
});
