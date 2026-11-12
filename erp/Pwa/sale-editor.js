'use strict';
// Novo lançamento (venda paga na hora), em uma tela só: itens, pagamento e cliente opcional. O servidor confere e grava como no computador.
window.CacpSaleEditor=function(h){
 const {el,button,link,heading,section,money,request,state,fail,can,render,phoneText}=h;
 const PAY=[['PIX','PIX','ok'],['Dinheiro','Dinheiro','warn'],['Débito','Cartão de débito','info'],['Crédito','Cartão de crédito 1x','violet']];
 let saved=null;
 const blank=()=>({operationId:crypto.randomUUID(),paymentId:crypto.randomUUID(),items:[],pay:'PIX',received:null,given:'',name:'',contact:'',discount:0,discountLabel:'',showCustomer:false,extraOk:false,result:null,uncertain:false});
 const cents=v=>Math.round((v+Number.EPSILON)*100)/100;
 const parse=t=>{const s=String(t??'').trim().replace(/\s|R\$/g,'');const n=Number(s.includes(',')?s.replace(/\./g,'').replace(',','.'):s);return Number.isFinite(n)?n:NaN;};
 const show=v=>new Intl.NumberFormat('pt-BR',{minimumFractionDigits:2,maximumFractionDigits:4}).format(v);
 // Mesma regra de QuantityPricing.UnitPrice no servidor: tabela exata → faixa → preço base.
 function priceOf(p,q){if(q<=0)return null;if(p.exactTable){const e=p.exact.find(x=>x.quantity===q);return e?e.total/q:q===1?p.basePrice:null;}const t=p.tiers.find(x=>x.from<=q&&(x.through==null||q<=x.through));return t?t.unitPrice:p.basePrice;}
 const discounted=(u,d)=>u==null||d<=0?u:Math.round(u*(100-d)/100*10000)/10000;
 function initialQuantity(p){return p.exactTable&&p.exact.length===1?p.exact[0].quantity:1;}
 const lineTotal=i=>cents((i.unit??0)*i.q);
 const total=()=>cents(saved.items.reduce((n,i)=>n+lineTotal(i),0));
 const received=()=>saved.received===null?total():parse(saved.received);
 const phoneOk=t=>{const v=String(t||'').trim(),d=v.replace(/\D/g,'');if(!d)return true;if(v.startsWith('+'))return d.length>=8&&d.length<=15;const n=d.length>=12&&d.startsWith('55')?d.slice(2):d;return n.length===10||n.length===11;};
 function thumb(x){const box=el('div','sale-thumb');if(x?.photo){const img=el('img');img.loading='lazy';img.alt='';img.src='/api/v1/produtos/'+x.id+'/miniatura';img.addEventListener('error',()=>img.remove());box.append(img);}else box.append(el('span','',x?.id?'sem foto':'avulso'));return box;}
 function addProduct(p){const at=saved.items.find(i=>i.product?.id===p.id);if(at){at.q++;reprice(at);}else{const item={key:crypto.randomUUID(),product:p,q:initialQuantity(p),manual:false};reprice(item);saved.items.push(item);}saved.received=null;saved.extraOk=false;}
 function reprice(i){if(i.manual||!i.product)return;i.unit=discounted(priceOf(i.product,i.q),saved.discount);}
 async function edit(target,signal){
  heading(target,'Novo lançamento','Venda paga na hora, entra no caixa.','#inicio');
  if(!can('finance.receive')){state(target,'Acesso restrito','Você não tem permissão para lançar vendas.','permission');return;}
  const options=await request('/api/v1/lancamentos/opcoes',{signal});if(signal.aborted)return;
  if(!saved||saved.result)saved=blank();
  if(!options.cashOpen)target.append(el('p','message error','Caixa fechado. Abra o caixa no computador principal para lançar.'));
  const box=el('div','sale-editor');target.append(box);
  const itemsCard=section('Itens'),chips=el('div','sale-chips'),searchInput=el('input'),results=el('div','picker-results'),list=el('div','sale-items');
  searchInput.type='search';searchInput.placeholder='Buscar produto ou digitar item';searchInput.maxLength=200;searchInput.setAttribute('aria-label','Buscar produto');searchInput.className='sale-search';
  itemsCard.append(chips,searchInput,results,list);
  const payCard=section('Pagamento'),tiles=el('div','pay-tiles'),payBody=el('div','sale-pay-body'),[receivedField,receivedInput]=moneyField('Valor recebido (R$)'),[givenField,givenInput]=moneyField('Cliente entregou (opcional)'),payNotes=el('div','sale-notes');
  const fields=el('div','sale-two');fields.append(receivedField,givenField);payBody.append(fields,payNotes);
  const allowed=PAY.filter(([,n])=>options.methods.some(m=>m.name===n));
  const others=el('select');others.setAttribute('aria-label','Outras formas de pagamento');{const first=el('option','','Outras formas…');first.value='';others.append(first);for(const m of options.methods)if(!allowed.some(([,n])=>n===m.name)){const o=el('option','',m.name);o.value=m.name;others.append(o);}}
  const marks=allowed.map(([text,name,tone])=>{const t=button(text,()=>{saved.pay=name;others.value='';sync();},'pay-tile '+tone);tiles.append(t);return [t,name];});
  others.addEventListener('change',()=>{if(others.value){saved.pay=others.value;sync();}});
  payCard.append(tiles,others,payBody);
  const customerCard=section('Cliente'),customerBody=el('div','sale-customer'),feedback=el('p','message');feedback.hidden=true;customerCard.append(customerBody);
  box.append(itemsCard,payCard,customerCard,feedback);
  const footer=el('div','command-footer sale-footer'),totalBox=el('div','sale-total'),totalValue=el('strong'),save=button('Salvar lançamento',submit,'primary');totalBox.append(el('span','','Total'),totalValue);footer.append(totalBox,save);target.append(footer);
  // Mais vendidos: um toque adiciona ou tira o item.
  function drawChips(){chips.replaceChildren();for(const p of options.top){const on=saved.items.some(i=>i.product?.id===p.id);const c=button((on?'✓ ':'')+p.shortName,()=>{if(on)saved.items=saved.items.filter(i=>i.product?.id!==p.id);else addProduct(p);saved.received=null;saved.extraOk=false;drawItems();sync();},'sale-chip'+(on?' on':''));chips.append(c);}chips.hidden=!options.top.length;}
  function drawItems(){drawChips();list.replaceChildren();if(!saved.items.length)list.append(el('p','muted small sale-empty','Toque num mais vendido ou busque o produto.'));
   saved.items.forEach(i=>{const row=el('div','sale-item'),head=el('div','sale-item-head'),info=el('div','sale-item-info');info.append(el('strong','',i.product?.name||i.description));const hint=el('p','small muted');const noPrice=i.unit==null;hint.textContent=noPrice?(i.product?'Quantidade sem preço tabelado: informe o preço':'Informe o preço'):i.manual?'Preço informado':i.product?.exactTable?'Preço tabelado':i.product?.tiers?.length?'Preço por quantidade':'';if(saved.discount>0&&!i.manual&&i.product)hint.textContent+=(hint.textContent?' · ':'')+saved.discountLabel+' −'+show(saved.discount)+'%';info.append(hint);head.append(thumb(i.product),info);
    const line=el('div','sale-line'),stepper=el('div','sale-stepper'),minus=button('−',()=>{if(i.q>1){i.q--;changed(i);}},'sale-step'),plus=button('+',()=>{i.q++;changed(i);},'sale-step'),q=el('input');minus.setAttribute('aria-label','Menos');plus.setAttribute('aria-label','Mais');q.inputMode='numeric';q.value=i.q;q.setAttribute('aria-label','Quantidade');q.addEventListener('change',()=>{const n=parseInt(q.value,10);i.q=n>0&&n<=1000000?n:i.q;changed(i);});stepper.append(minus,q,plus);
    const [unitField,unit]=moneyField('Unitário');unit.value=i.unit==null?'':show(i.unit);unit.addEventListener('change',()=>{const n=parse(unit.value);if(Number.isFinite(n)&&n>=0){i.unit=n;i.manual=true;}else if(!unit.value.trim()){i.manual=false;reprice(i);}saved.received=null;saved.extraOk=false;drawItems();sync();});
    const sub=el('strong','sale-sub',money(lineTotal(i))),remove=button('✕',()=>{saved.items=saved.items.filter(x=>x!==i);saved.received=null;saved.extraOk=false;drawItems();sync();},'sale-remove');remove.setAttribute('aria-label','Remover item');
    line.append(stepper,unitField,sub,remove);row.append(head,line);list.append(row);});}
  function changed(i){reprice(i);saved.received=null;saved.extraOk=false;drawItems();sync();}
  function sync(){const T=total(),R=received(),valid=Number.isFinite(R)&&R>=0,over=valid?cents(R-T):0,missing=valid?cents(T-R):0;totalValue.textContent=money(T);
   for(const [t,name] of marks)t.setAttribute('aria-pressed',String(saved.pay===name));if(document.activeElement!==receivedInput)receivedInput.value=saved.received===null?(T?show(T):''):saved.received;
   const cash=saved.pay==='Dinheiro';givenField.hidden=!cash;payNotes.replaceChildren();
   const given=parse(saved.given);if(cash&&saved.given&&Number.isFinite(given)){const change=cents(given-(valid?R:0));payNotes.append(note(change<0?'Faltam '+money(-change):'Troco: '+money(change),change<0?'err':''));}
   if(!valid)payNotes.append(note('Confira o valor recebido.','err'));else if(T>0&&R===0)payNotes.append(note('Sem pagamento: o valor todo fica em aberto em Devedores.','warn'));else if(missing>0)payNotes.append(note('Faltam '+money(missing)+'. O restante fica em aberto em Devedores.','warn'));
   if(over>0){const n=note(money(over)+' a mais que o total.','warn'),label=el('label','check-field'),check=el('input');check.type='checkbox';check.checked=saved.extraOk;check.addEventListener('change',()=>{saved.extraOk=check.checked;updateSave();});label.append(check,el('span','',feeOf(saved.pay)>0?'Confirmo o acréscimo de cobrança':'Cliente deixou o troco (valor adicional)'));n.append(label);payNotes.append(n);}
   function updateSave(){save.disabled=!options.cashOpen||T<=0||!valid||saved.items.some(i=>i.unit==null)||!phoneOk(saved.contact)||(over>0&&!saved.extraOk);}updateSave();}
  const feeOf=name=>options.methods.find(m=>m.name===name)?.feePercent??0;
  function drawCustomer(){customerBody.replaceChildren();if(!saved.showCustomer&&!saved.name&&!saved.contact){customerBody.append(button('+ Informar nome e telefone',()=>{saved.showCustomer=true;drawCustomer();customerBody.querySelector('input')?.focus();},'soft'));return;}
   const [nameField,nameInput]=textField('Nome'),[phoneField,phoneInput]=textField('Telefone / WhatsApp'),phoneNote=el('div','sale-notes'),found=el('div','picker-results');nameInput.value=saved.name;nameInput.maxLength=200;nameInput.autocomplete='off';nameInput.placeholder='Vazio = Não informado';phoneInput.value=saved.contact;phoneInput.type='tel';phoneInput.inputMode='tel';phoneInput.placeholder='(11) 90000-0000';phoneInput.maxLength=40;customerBody.append(nameField,found,phoneField,phoneNote);
   const checkPhone=()=>{phoneNote.replaceChildren();if(!phoneOk(saved.contact))phoneNote.append(note('Telefone incompleto: informe com DDD ou deixe em branco.','err'));sync();};
   let timer,controller;async function look(term){found.replaceChildren();controller?.abort();clearTimeout(timer);if(!options.customers||term.trim().length<2)return;timer=setTimeout(async()=>{controller=new AbortController();const c=controller;try{const r=await request('/api/v1/lancamentos/clientes?search='+encodeURIComponent(term.trim()),{signal:c.signal});if(signal.aborted||c.signal.aborted)return;found.replaceChildren();for(const x of r.items){const b=button('',()=>{saved.name=x.name;saved.contact=x.contact;saved.discount=x.discount||0;saved.discountLabel=x.priceType||'';for(const i of saved.items)reprice(i);saved.received=null;saved.extraOk=false;drawCustomer();drawItems();sync();},'picker-option');b.append(el('strong','',x.name),el('span','small muted',phoneText(x.contact)||'Sem telefone'));found.append(b);}}catch(e){if(e.status===401)fail(target,e,render);}},300);}
   nameInput.addEventListener('input',()=>{saved.name=nameInput.value;if(saved.discount){saved.discount=0;saved.discountLabel='';for(const i of saved.items)reprice(i);saved.received=null;drawItems();sync();}look(nameInput.value);});phoneInput.addEventListener('input',()=>{saved.contact=phoneInput.value;checkPhone();look(phoneInput.value);});checkPhone();}
  // Busca: produto do cadastro ou item avulso digitado.
  let timer,controller;searchInput.addEventListener('input',()=>{results.replaceChildren();controller?.abort();clearTimeout(timer);const term=searchInput.value.trim();if(!term)return;timer=setTimeout(async()=>{controller=new AbortController();const c=controller;try{const r=await request('/api/v1/lancamentos/produtos?search='+encodeURIComponent(term),{signal:c.signal});if(signal.aborted||c.signal.aborted)return;results.replaceChildren();const pick=()=>{searchInput.value='';results.replaceChildren();saved.received=null;saved.extraOk=false;drawItems();sync();};for(const p of r.items){const b=button('',()=>{addProduct(p);pick();},'picker-option sale-option'),u=discounted(priceOf(p,initialQuantity(p)),saved.discount);const text=el('span','sale-option-text');text.append(el('strong','',p.name),el('span','small muted',u==null?'Preço a informar':money(u)+(initialQuantity(p)>1?' /un. em '+initialQuantity(p)+' un.':'')));b.append(thumb(p),text);results.append(b);}const loose=button('',()=>{saved.items.push({key:crypto.randomUUID(),product:null,description:term,q:1,unit:null,manual:true});pick();},'picker-option');loose.append(el('strong','','Lançar "'+term+'" como item avulso'));results.append(loose);}catch(e){if(e.status===401)fail(target,e,render);}},250);});
  signal.addEventListener('abort',()=>{clearTimeout(timer);controller?.abort();},{once:true});
  receivedInput.addEventListener('input',()=>{saved.received=receivedInput.value;saved.extraOk=false;sync();});receivedInput.addEventListener('blur',()=>{if(saved.received!==null&&!saved.received.trim()){saved.received='0';}sync();});givenInput.value=saved.given;givenInput.addEventListener('input',()=>{saved.given=givenInput.value;sync();});
  async function submit(){const T=total(),R=received();if(!navigator.onLine){showFeedback('Conecte-se para salvar este lançamento.','error');return;}
   const over=cents(R-T);const body=saved.uncertain&&saved.body?saved.body:{operationId:saved.operationId,customerName:saved.name.trim(),contact:saved.contact.trim(),items:saved.items.map(i=>({productId:i.product?.id??null,description:i.product?null:i.description,quantity:i.q,unitPrice:i.unit})),payments:R>0?[{id:saved.paymentId,paymentType:saved.pay,amount:cents(R),...(over>0?{excessConfirmed:true,excessKind:feeOf(saved.pay)>0?'Acréscimo de cobrança':'Valor adicional do cliente'}:{})}]:[]};
   saved.body=body;save.disabled=true;save.textContent='Salvando…';showFeedback('','');
   try{const r=await request('/api/v1/lancamentos',{method:'POST',body,signal});if(signal.aborted)return;saved.result=r;saved.uncertain=false;done(target);}
   catch(e){if(signal.aborted)return;if(e.status===401){await fail(target,e,render);return;}save.textContent='Salvar lançamento';save.disabled=false;
    if(e.status===0||e.status>=500){saved.uncertain=true;box.querySelectorAll('input,select,button').forEach(n=>n.disabled=true);save.textContent='Tentar de novo (sem duplicar)';showFeedback('Não foi possível confirmar. Toque em tentar de novo: o mesmo lançamento não é gravado duas vezes.','error');return;}
    showFeedback(e.message||'Não foi possível salvar.','error');}}
  function showFeedback(text,kind){feedback.hidden=!text;feedback.textContent=text;feedback.className='message'+(kind?' '+kind:'');}
  drawItems();drawCustomer();sync();
  if(saved.uncertain){box.querySelectorAll('input,select,button').forEach(n=>n.disabled=true);save.disabled=false;save.textContent='Tentar de novo (sem duplicar)';showFeedback('O último envio não foi confirmado. Toque em tentar de novo: o mesmo lançamento não é gravado duas vezes.','error');}
 }
 function moneyField(label){const box=el('label','field',label),input=el('input');input.inputMode='decimal';input.autocomplete='off';box.append(input);return [box,input];}
 function textField(label){const box=el('label','field',label),input=el('input');input.type='text';box.append(input);return [box,input];}
 function note(text,kind){return el('div','sale-note '+kind,text);}
 // Recibo em texto: abre o WhatsApp do cliente (sem telefone, o WhatsApp pede o contato).
 function receiptText(r){const lines=(r.company?[r.company]:[]).concat(['Recibo de compra'],r.items.map(i=>i.quantity+' × '+i.description+' = '+money(i.subtotal)),['Total: '+money(r.total)],r.payments.map(p=>'Pago em '+p.paymentType+': '+money(p.amount)));if(r.remaining>0)lines.push('Falta pagar: '+money(r.remaining));lines.push('Obrigado pela preferência!');return lines.join('\n');}
 function whatsappUrl(r){const text=encodeURIComponent(receiptText(r));let n=String(r.contact||'').replace(/\D/g,'');if(n.length===10||n.length===11)n='55'+n;return n.length>=12?'https://wa.me/'+n+'?text='+text:'https://wa.me/?text='+text;}
 function done(target){const r=saved.result;target.replaceChildren();heading(target,'Lançamento salvo','Já está no caixa.','#inicio');const card=section('Resumo','confirmation');card.append(el('p','confirmation-mark','✓'),el('p','sale-done-value',money(r.received)),el('p','muted',(r.payments.map(p=>p.paymentType).join(' + ')||'Sem pagamento')+' · '+r.items.length+(r.items.length===1?' item':' itens')+' · '+r.customer));if(r.excess>0)card.append(note(money(r.excess)+' registrados acima do total.','warn'));if(r.remaining>0)card.append(note(money(r.remaining)+' ficaram em Devedores.','warn'));
  const actions=el('div','action-links sale-done-actions');actions.append(button('Lançar outro',()=>{saved=null;render();},'primary'));const send=link('Enviar recibo pelo WhatsApp',whatsappUrl(r),'button soft');send.target='_blank';send.rel='noopener noreferrer';actions.append(send,link('Ver lançamentos do caixa','#caixa','button soft'));target.append(card,actions);}
 return {edit};
};
