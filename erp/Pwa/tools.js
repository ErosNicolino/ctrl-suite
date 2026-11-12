'use strict';
// Calculadora de produção e Orçamento no celular. Carregado só quando a tela é aberta (não pesa nas demais).
// As contas são feitas no servidor com o mesmo motor do desktop; aqui ficam formulário, resultado e histórico.
window.CacpTools = function(h) {
  const {el,button,heading,section,money,request,state,fail,toast,share}=h;
  const HISTORY='cacp-calc-history';
  function history(){try{return JSON.parse(localStorage.getItem(HISTORY)||'[]');}catch{return [];}}
  function remember(line){try{localStorage.setItem(HISTORY,JSON.stringify([line,...history()].slice(0,10)));}catch{}}
  // "Seu custo" do orçamento: só chega do servidor para quem pode alterar custos. Não entra no PDF nem no texto do WhatsApp.
  function costBox(r){const c=r.cost,n=v=>new Intl.NumberFormat('pt-BR',{maximumFractionDigits:2}).format(v),minWins=c.minimum>=c.bySize,box=el('section','cost-box'),head=el('div','cost-head'),rows=el('dl','cost-rows');head.append(el('strong','','Seu custo'),el('span','cost-tag','interno · não vai no PDF nem no resumo'));
    for(const [label,value,off] of [[`Custo pelo tamanho (${n(r.area)} m² × ${money(c.rate)} ÷ ${n(c.multiplier)})`,money(c.bySize),minWins],['Custo mínimo (saída mínima)',money(c.minimum),!minWins]]){const d=el('div','cost-row'+(off?' off':''));d.append(el('dt','',label),el('dd','',value));rows.append(d);}
    const total=el('div','cost-total');total.append(el('span','','Custo real · '+(minWins?'vale o mínimo':'vale o tamanho')),el('strong','',money(c.total)));
    const gain=el('div','cost-total gain');gain.append(el('span','','Ganho estimado'),el('strong','',money(c.gain)+(r.recommended>0?' · '+Math.round(c.gain/r.recommended*100)+'%':'')));
    box.append(head,rows,total,gain);return box;}
  function input(label,{name,value='',mode='decimal',placeholder=''}={}){const field=el('label','field',label),i=el('input');i.name=name;i.value=value;i.inputMode=mode;i.placeholder=placeholder;i.autocomplete='off';i.enterKeyHint='go';field.append(i);return [field,i];}
  function choice(label,options,value){const field=el('label','field',label),s=el('select');for(const [k,t] of options){const o=el('option','',t);o.value=k;s.append(o);}s.value=value;field.append(s);return [field,s];}
  function segmented(options,value,changed){const bar=el('div','segmented');bar.setAttribute('role','tablist');for(const [k,t] of options){const b=button(t,()=>{for(const x of bar.children)x.setAttribute('aria-selected',String(x===b));changed(k);},'segment');b.setAttribute('role','tab');b.setAttribute('aria-selected',String(k===value));bar.append(b);}return bar;}
  // Faixa fixa no topo do formulário (só celular): mostra o resultado enquanto se digita; toque leva ao detalhe.
  function quoteBar(result){const bar=button("",()=>result.scrollIntoView({behavior:"smooth",block:"start"}),"quote-bar");bar.hidden=true;bar.set=(label,value)=>{bar.hidden=!value;bar.replaceChildren(el("span","small",label),el("strong","",value||""));};return bar;}
  function resultCard(){const card=el('section','card result-card');card.setAttribute('aria-live','polite');card.append(el('p','eyebrow','Resultado'),el('strong','result-hero','—'),el('p','muted small','Preencha os dados para calcular.'));return card;}

  async function calculator(target,signal){
    heading(target,'Calculadora','Folhas A4, adesivo em m² e área fixa.');
    let mode='a4';const form=el('section','card calc-form'),result=resultCard(),past=section('Últimos cálculos'),actions=el('div','action-links');
    const [shapeField,shape]=choice('Formato',[['quadrado','Quadrado'],['retangular','Retangular'],['redondo','Redondo']],'quadrado');
    const [wField,w]=input('Medida (cm)',{name:'largura',value:'5'}),[hField,hh]=input('Altura (cm)',{name:'altura',value:'5'}),[qField,q]=input('Quantidade desejada',{name:'quantidade',value:'100',mode:'numeric'});
    const [mField,max]=input('Largura máxima neste cálculo (cm)',{name:'larguraMaxima',value:'120'}),[awField,aw]=input('Largura disponível (cm)',{name:'areaLargura',value:'100'}),[ahField,ah]=input('Altura disponível (cm)',{name:'areaAltura',value:'100'});
    // Adesivo tem os dois cálculos do desktop: material necessário ou quantos cabem numa área (100 × 100, bobina padrão em m², personalizado).
    const [opField,op]=choice('Cálculo',[['material','Material necessário'],['area','Quantos cabem na área?']],'material'),[pField,preset]=choice('Formato da área disponível',[['100','100 × 100 cm'],['bobina','Usar largura padrão da bobina'],['personalizado','Personalizado']],'100'),[m2Field,m2]=input('Área disponível (m²)',{name:'areaM2',value:'1'});
    const tabs=segmented([['a4','Folha A4'],['adesivo','Adesivo m²'],['area','Área fixa']],mode,k=>{mode=k;layout();calculate();});
    form.append(tabs,opField,shapeField,wField,hField,qField,mField,pField,m2Field,awField,ahField);const bar=quoteBar(result);form.prepend(bar);
    function layout(){const rect=shape.value==='retangular',inverse=mode==='adesivo'&&op.value==='area';wField.firstChild.textContent=rect?'Largura (cm)':shape.value==='redondo'?'Diâmetro (cm)':'Lado (cm)';hField.hidden=!rect;opField.hidden=mode!=='adesivo';qField.hidden=mode==='area'||inverse;mField.hidden=mode!=='adesivo'||inverse;pField.hidden=!inverse;m2Field.hidden=!inverse||preset.value!=='bobina';awField.hidden=ahField.hidden=!(mode==='area'||inverse&&preset.value==='personalizado');}
    let last=null,timer,controller;
    function showHistory(){past.replaceChildren(el('h2','','Últimos cálculos'));const items=history();if(!items.length)past.append(el('p','muted small','Os cálculos feitos aqui aparecem nesta lista.'));for(const line of items){const b=button(line,()=>{navigator.clipboard?.writeText(line).then(()=>toast('Cálculo copiado.'),()=>{});},'history-line');past.append(b);}}
    // A espera de 350 ms é só para a digitação; ao abrir a tela calcula na hora (delay 0).
    async function calculate(commit=false,delay=350){
      clearTimeout(timer);timer=setTimeout(async()=>{
        controller?.abort();controller=new AbortController();const current=controller;
        const params=new URLSearchParams({modo:mode,formato:shape.value,largura:w.value,altura:hh.value,quantidade:q.value,larguraMaxima:max.value,areaLargura:aw.value,areaAltura:ah.value,calculo:op.value,preset:preset.value,areaM2:m2.value});
        try{const r=await request('/api/v1/calculadora?'+params,{signal:current.signal});if(signal.aborted||current.signal.aborted)return;last=r;
          result.replaceChildren(el('p','eyebrow',r.status));const hero=el('strong','result-hero',r.metrics[0].value);bar.set(r.metrics[0].label,r.metrics[0].value);result.append(el('p','small muted',r.metrics[0].label),hero,el('p','small',r.summary));const grid=el('dl','info-grid');for(const m of r.metrics.slice(1)){const c=el('div','info-cell');c.append(el('dt','',m.label),el('dd','',m.value));grid.append(c);}result.append(grid);if(r.details)result.append(el('p','small muted',r.details));copy.disabled=false;
          if(commit)remember(new Intl.DateTimeFormat('pt-BR',{day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'}).format(new Date())+' · '+r.summary+' → '+r.metrics[0].label+': '+r.metrics[0].value);showHistory();}
        catch(error){if(signal.aborted||current.signal.aborted)return;last=null;copy.disabled=true;bar.set("","");result.replaceChildren(el('p','eyebrow','Resultado'),el('strong','result-hero muted','—'),el('p','message error',error.message));}
      },delay);
    }
    const copy=button('Copiar resultado',()=>{if(!last)return;const text=last.status+': '+last.summary+' → '+last.metrics.map(m=>m.label+': '+m.value).join(' · ');remember(last.summary+' → '+last.metrics[0].label+': '+last.metrics[0].value);showHistory();navigator.clipboard?.writeText(text).then(()=>toast('Resultado copiado.'),()=>toast('Não foi possível copiar.'));},'soft');copy.disabled=true;actions.append(copy);
    for(const x of [op,preset,shape,w,hh,q,max,m2,aw,ah])x.addEventListener(x.tagName==='SELECT'?'change':'input',()=>{layout();calculate();});
    form.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();calculate(true);document.activeElement?.blur();}});
    const grid=el('div','tool-grid');grid.append(form,el('div','stack'));grid.lastChild.append(result,actions,past);target.append(grid);layout();showHistory();calculate(false,0);
  }


  // Orçamento: as mesmas ações do desktop — calcular, copiar resumo para o WhatsApp, PDF de prévia (vários itens),
  // criar pedido com este item e consultar os prazos de produção. Editar preços e prazos continua só no desktop.
  async function budget(target,signal){
    heading(target,'Orçamento','Calcule por medida ou consulte os prazos de produção.');
    const view=el('div');const tabs=segmented([['calc','Calcular'],['prazos','Prazos de produção']],'calc',k=>{view.replaceChildren();if(k==='calc')calc(view);else deadlines(view);});
    target.append(tabs,view);await calc(view);

    async function calc(host){
      const body=el('div');host.append(body);state(body,'Carregando produtos','','loading');
      let rules;try{rules=await request('/api/v1/orcamento/regras',{signal});}catch(error){if(!signal.aborted)await fail(body,error,()=>{host.replaceChildren();calc(host);});return;}if(signal.aborted)return;body.remove();
      const form=el('section','card calc-form'),result=resultCard();
      const [pField,product]=choice('Produto',rules.map(x=>[x.id,x.product]),rules[0]?.id||'');
      const [uField,unit]=choice('Unidade das medidas',[['m','Metros'],['cm','Centímetros']],'m');
      const [wField,w]=input('Largura (m)',{name:'largura',value:'0,90'}),[hField,hh]=input('Altura (m)',{name:'altura',value:'1,20'}),[qField,q]=input('Quantidade',{name:'quantidade',value:'1',mode:'numeric'});
      form.append(pField,uField,wField,hField,qField,el('p','small accent-text','Adicional de arte sempre incluído, mesmo com arte pronta.'));const bar=quoteBar(result);form.prepend(bar);
      let last=null,timer,controller;
      function labels(){const u=unit.value;wField.firstChild.textContent='Largura ('+u+')';hField.firstChild.textContent='Altura ('+u+')';}
      function calculate(delay=300){clearTimeout(timer);timer=setTimeout(async()=>{controller?.abort();controller=new AbortController();const current=controller;
        try{const r=await request('/api/v1/orcamento/calcular?'+new URLSearchParams({regra:product.value,largura:w.value,altura:hh.value,unidade:unit.value,quantidade:q.value}),{signal:current.signal});if(signal.aborted||current.signal.aborted)return;last=r;
          result.replaceChildren(el('p','eyebrow','Valor para orçar'),el('strong','result-hero',money(r.recommended)));bar.set("Valor para orçar",money(r.recommended));const grid=el('dl','info-grid');for(const [label,value] of [['Área',new Intl.NumberFormat('pt-BR',{minimumFractionDigits:2,maximumFractionDigits:2}).format(r.area)+' m²'],['Preço pelo tamanho',money(r.sizePrice)],['Frete interno + arte',money(r.fees)],['Venda mínima oficial',money(r.minimum)]]){const c=el('div','info-cell');c.append(el('dt','',label),el('dd','',value));grid.append(c);}result.append(grid);if(r.cost)result.append(costBox(r));const how=el('details','composition');how.append(el('summary','','Como o valor é calculado'),el('p','small contract',r.formula));result.append(how);for(const b of [pdf,copy,order])b.disabled=false;}
        catch(error){if(signal.aborted||current.signal.aborted)return;last=null;for(const b of [pdf,copy,order])b.disabled=true;bar.set("","");result.replaceChildren(el('p','eyebrow','Valor para orçar'),el('strong','result-hero muted','—'),el('p','message error',error.message));}},delay);}
      const copy=button('Copiar resumo',()=>{if(!last)return;navigator.clipboard?.writeText(last.whatsapp).then(()=>toast('Resumo copiado. Cole no WhatsApp do cliente.'),()=>toast('Não foi possível copiar.'));},'soft');
      const pdf=button('PDF de prévia',()=>{if(last)preview([{quantity:last.quantity,product:last.description,unitPrice:last.recommended/last.quantity}]);},'primary');
      const order=button('Criar pedido',()=>{if(!last)return;try{sessionStorage.setItem('cacp-order-seed',JSON.stringify({description:last.orderItem,quantity:last.quantity,unitPrice:last.recommended/last.quantity}));}catch{}location.hash='#pedidos/novo';},'soft');
      order.hidden=!(h.can('orders.create')&&h.can('orders.value')&&h.can('view.customers')&&h.can('view.products'));
      for(const b of [pdf,copy,order])b.disabled=true;
      const actions=el('div','action-links');actions.append(pdf,order,copy);
      for(const x of [product,unit,w,hh,q])x.addEventListener(x.tagName==='SELECT'?'change':'input',()=>{labels();calculate();});
      form.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();calculate();document.activeElement?.blur();}});
      const grid=el('div','tool-grid');grid.append(form,el('div','stack'));grid.lastChild.append(result,actions);host.append(grid);labels();calculate(0);
    }

    async function deadlines(host){
      const body=el('div');host.append(body);state(body,'Carregando prazos','','loading');
      let rows;try{rows=await request('/api/v1/orcamento/prazos',{signal});}catch(error){if(!signal.aborted)await fail(body,error,()=>{host.replaceChildren();deadlines(host);});return;}if(signal.aborted)return;body.remove();
      host.append(el('p','message','A contagem do prazo começa após a aprovação da arte.'));
      const search=el('input');search.type='search';search.placeholder='Pesquisar produto ou prazo';search.setAttribute('aria-label','Pesquisar prazos');const filters=el('div','filters');filters.append(search);host.append(filters);
      const list=el('div','list');host.append(list);
      function draw(){const t=search.value.trim().toLowerCase();list.replaceChildren();const found=rows.filter(x=>!t||(x.description+' '+x.deadline+' '+x.note).toLowerCase().includes(t));
        for(const x of found){const row=el('article','list-row');row.append(el('strong','',x.description),el('p','accent-text',x.deadline));if(x.note)row.append(el('p','small muted',x.note));list.append(row);}
        if(!found.length)state(list,'Nenhum prazo encontrado','Tente outro termo.');}
      search.addEventListener('input',draw);draw();
    }
  }

  // Prévia: cliente e itens num painel inferior (como no desktop, dá para incluir mais itens). O PDF é gerado no servidor,
  // não fica salvo e vai para Compartilhar (WhatsApp).
  function preview(seed){
    const sheet=h.sheet('Prévia de orçamento');const form=el('form','sheet-form');
    const [nField,name]=input('Nome ou razão social do cliente *',{name:'cliente',mode:'text',placeholder:'Nome ou razão social'}),[dField,doc]=input('CPF/CNPJ (opcional)',{name:'documento',mode:'numeric'}),[cField,contact]=input('Telefone / WhatsApp',{name:'telefone',mode:'tel',placeholder:'(11) 90000-0000'});
    const items=el('div','stack preview-items'),total=el('p','preview-total');
    function itemRow(x){const row=el('fieldset','card preview-item');const pf=el('label','field','Produto ou serviço'),p=el('textarea');p.rows=2;p.maxLength=300;p.value=x.product;pf.append(p);const [qf,qq]=input('Quantidade',{name:'qtd',mode:'numeric',value:String(x.quantity)});const [uf,u]=input('Preço unitário (R$)',{name:'preco',value:x.unitPrice==null?'':new Intl.NumberFormat('pt-BR',{minimumFractionDigits:2,maximumFractionDigits:6}).format(x.unitPrice)});
      const remove=button('Remover item',()=>{row.remove();sum();},'text-button');const pair=el('div','pair');pair.append(qf,uf);row.append(pf,pair,remove);row.read=()=>({quantity:parseInt(qq.value,10),product:p.value.trim(),unitPrice:Number(u.value.replace(/\./g,'').replace(',','.'))});for(const i of [qq,u])i.addEventListener('input',sum);items.append(row);}
    function read(){return [...items.children].map(r=>r.read());}
    function sum(){const t=read().reduce((a,x)=>a+(x.quantity>0&&x.unitPrice>=0?x.quantity*x.unitPrice:0),0);total.textContent='Total do orçamento: '+money(t);}
    for(const x of seed)itemRow(x);
    const add=button('Adicionar item',()=>{itemRow({quantity:1,product:'',unitPrice:null});items.lastChild.querySelector('textarea').focus();sum();},'soft');
    const notesField=el('label','field','Observações, prazo e condições'),notes=el('textarea');notes.maxLength=2000;notesField.append(notes);
    const feedback=el('p','message error');const submit=el('button','button primary wide','Gerar e compartilhar');submit.type='submit';
    form.append(nField,dField,cField,el('h3','','Produtos e serviços'),items,add,total,notesField,feedback,submit);sheet.body.append(form);sheet.open();sum();name.focus();
    form.addEventListener('submit',async e=>{e.preventDefault();feedback.textContent='';if(!name.value.trim()){feedback.textContent='Informe o nome do cliente.';name.focus();return;}
      const list=read();if(!list.length||list.some(x=>!x.product||!(x.quantity>0)||!(x.unitPrice>=0))){feedback.textContent='Confira os itens: produto, quantidade e preço.';return;}
      submit.disabled=true;submit.textContent='Gerando PDF…';
      try{const blob=await h.post('/api/v1/orcamento/previa',{customer:name.value.trim(),document:doc.value.trim(),contact:contact.value.trim(),notes:notes.value.trim(),items:list});
        await share(blob,'Orcamento_'+name.value.trim().replace(/[^\p{L}\p{N}]+/gu,'_').slice(0,40)+'.pdf','Orçamento');sheet.close();}
      catch(error){feedback.textContent=error.message;}finally{submit.disabled=false;submit.textContent='Gerar e compartilhar';}});
  }
  return {calculator,budget};
};
