/* Cookie-scoped campaign controller. Mutations are single-flight and never replayed. */
window.CampaignClient = (() => {
  let active=false,generation=0,state=null,pending=false,loading=false,timer=null,readController=null,readSerial=0,renderKey='',screenKey='';
  let message='',connectionLost=false,lastFeedback='';
  const root=()=>document.querySelector('#campaign-view');
  const content=()=>document.querySelector('#campaign-content');
  const cap=key=>state?.capabilities?.[key];
  function announce(text,error=false) {
    message=text;
    const status=document.querySelector('#campaign-status');
    status.textContent=text;status.classList.toggle('error',error);
  }
  function stop() {clearTimeout(timer);timer=null;}
  function leave() {
    active=false;generation++;readSerial++;stop();readController?.abort();readController=null;
    CampaignCards.close(false);
  }
  async function open() {
    leave();active=true;loading=true;renderKey='';screenKey='';message='';state=null;connectionLost=false;
    document.querySelector('#campaign-refresh').onclick=()=>refresh();
    render();await refresh();schedule();
  }
  async function request(suffix,method='GET',body=null,signal) {
    const response=await fetch('/api/campaign'+suffix,{method,credentials:'same-origin',cache:'no-store',
      headers:{'Content-Type':'application/json'},...(body?{body:JSON.stringify(body)}:{}),signal});
    let data;try{data=await response.json();}catch{throw Error('The server returned an unreadable response. Refresh to check your progress.');}
    if(!response.ok){const error=Error(data.error||'Campaign request failed.');error.status=response.status;error.code=data.code;throw error;}
    return data;
  }
  function schedule() {
    stop();
    if(!active||document.hidden)return;
    timer=setTimeout(async()=>{await refresh({quiet:true});schedule();},state?.phase==='InMatch'&&!state.requiresContinue?800:2500);
  }
  async function refresh({quiet=false}={}) {
    if(!active||pending||document.hidden&&quiet)return;
    const myGeneration=generation,serial=++readSerial;
    readController?.abort();const controller=new AbortController();readController=controller;
    const timeout=setTimeout(()=>controller.abort(),8000);
    try{
      const next=await request('','GET',null,controller.signal);
      if(!active||myGeneration!==generation||serial!==readSerial)return;
      connectionLost=false;loading=false;adopt(next);
      if((!quiet&&!message)||message.startsWith('Could not refresh'))announce('Campaign ready.');
    }catch(error){
      if(!active||myGeneration!==generation||serial!==readSerial)return;
      loading=false;connectionLost=true;announce('Could not refresh the campaign. '+(error.name==='AbortError'?'The request timed out.':error.message)+' Use Refresh campaign to reconnect.',true);render();
    }finally{clearTimeout(timeout);if(readController===controller)readController=null;}
  }
  function adopt(next) {
    if(state&&next.version!==state.version)CampaignCards.close();
    const wasUnsaved=state?.saveStatus==='failed';
    state=next;
    if(next.saveStatus==='failed')announce('Progress is not saved. '+(next.notice||'Changes are retained in this session only. Refresh to check the save status.'),true);
    else if(next.feedback) {
      const text=next.feedback.message+(next.feedback.grants||[]).map(g=>' '+CampaignCards.feature(next,'Gumball',g.id).name+': '+(g.duplicate?'duplicate':'new unlock')+', '+g.copies+' '+(g.copies===1?'copy':'copies')+(g.bonus?', bid bonus +'+g.bonus:'.')).join('');
      if(text!==lastFeedback){lastFeedback=text;announce(text);}
    }else if(next.notice)announce(next.notice);
    else if(wasUnsaved)announce('Progress saved.');
    render();
  }
  function render() {
    if(!active)return;
    root().setAttribute('aria-busy',String(pending||loading));
    const refreshButton=document.querySelector('#campaign-refresh');
    refreshButton.disabled=pending||loading;
    const key=state?JSON.stringify(state):'loading';
    if(key!==renderKey) {
      const focused=document.activeElement?.dataset.focusKey;
      const detailKey=element=>element.querySelector('summary')?.textContent.split(' · ')[0];
      const openDetails=[...content().querySelectorAll('details[open]')].map(detailKey);
      const scroll=window.scrollY;
      content().innerHTML=state?CampaignScreens.render(state):'<header class="page-header"><div><h1 tabindex="-1">Loading campaign…</h1><p>Your saved progress is being checked.</p></div></header>';
      const footer=document.querySelector('#campaign-footer');
      footer.innerHTML=state?'<div class="campaign-actions">'+(state.hasRun||state.phase==='Recovery'?CampaignScreens.button('new','New replacement run',cap('new'))+CampaignScreens.button('delete','Delete saved run',cap('delete'),'danger'):'')
        +(state.phase==='InMatch'?CampaignScreens.button('forfeit','Forfeit match',cap('forfeit'),'danger'):'')
        +(state.hasRun?CampaignScreens.button('deck','View shared deck',{enabled:true}):'')+'</div><p class="campaign-prose">Leaving this view or closing the tab preserves your run. Forfeit is an explicit loss.</p>':'';
      bind(content());bind(footer);
      content().querySelectorAll('details').forEach(x=>{
        const summary=x.querySelector('summary');
        if(summary)summary.dataset.focusKey='disclosure:'+detailKey(x);
        if(openDetails.includes(detailKey(x)))x.open=true;
      });
      renderKey=key;
      const nextScreen=state?.requiresContinue?'continue':state?.phase||'loading';
      if(nextScreen!==screenKey){
        content().querySelector('h1')?.focus({preventScroll:true});screenKey=nextScreen;
      }else if(focused) {
        [...root().querySelectorAll('[data-focus-key]')].find(x=>x.dataset.focusKey===focused)?.focus({preventScroll:true});
        window.scrollTo(0,scroll);
        if(document.activeElement===document.body)content().querySelector('h1')?.focus({preventScroll:true});
      }
    }
    root().querySelectorAll('button[data-campaign-action],button[data-campaign-command],button.campaign-card').forEach(button=>{
      if(button.dataset.serverDisabled===undefined)button.dataset.serverDisabled=String(button.disabled);
      button.disabled=button.dataset.serverDisabled==='true'||pending||loading||connectionLost;
    });
    const warning=document.querySelector('#campaign-save-warning');
    warning.hidden=state?.saveStatus!=='failed';
    warning.textContent=state?.saveStatus==='failed'?'Progress is not saved. '+(state.notice||'Changes are retained in this session only.'): '';
  }
  async function mutate(route,body={}, {live=false,method='POST'}={}) {
    if(!active||pending||loading||connectionLost||!state)return;
    const myGeneration=generation;
    const data={expectedVersion:state.version,...body};
    if(live){data.expectedRevision=state.table.revision;data.expectedDealSerial=state.table.dealSerial;}
    pending=true;stop();readSerial++;readController?.abort();CampaignCards.close(false);render();
    let needsRefresh=false;
    try {
      const next=await request(route,method,data);
      if(active&&myGeneration===generation){announce('Saved.');adopt(next);}
    }catch(error) {
      if(active&&myGeneration===generation){
        CampaignCards.close();
        announce(error.status===409?'The table changed; choose again. Refreshing your campaign.':error.message+(error.status?'':' The request may have been accepted. Refreshing to check progress.'),true);
        needsRefresh=true;
      }
    }finally{
      // Keep old choices locked until failure reconciliation returns a fresh version.
      if(needsRefresh&&active&&myGeneration===generation)loading=true;
      pending=false;
      if(active&&myGeneration===generation){render();if(needsRefresh)await refresh({quiet:true});schedule();}
      else if(active){await refresh({quiet:true});schedule();}
    }
  }
  function targets(title,count,invoke,confirm,description='') {
    const cards=state.phase==='InMatch'?state.table?.players.find(p=>p.seat===0)?.visibleCards||[]:state.deck;
    CampaignCards.targets({title,cards,count,invoke,onConfirm:confirm,description});
  }
  function useItem(button) {
    const category=button.dataset.category,slot=Number(button.dataset.slot);
    const item=(category==='Tarot'?state.inventory.tarot:state.inventory.sigils).find(x=>x.slot===slot);
    if(!item)return;
    const itemFeature=CampaignCards.feature(state,category,item.id);
    const name=itemFeature.name;
    targets('Use '+name,item.targetCount,button,targetIds=>{
      if(state.phase==='InMatch')mutate('/commands',{command:category==='Tarot'?{kind:'use-tarot',slot,targetIds}:{kind:'use-sigil',slot,targetId:targetIds[0]}},{live:true});
      else mutate('/items/use',{category,slot,targetIds});
    },itemFeature.description);
  }
  function action(button) {
    if(pending||loading||connectionLost)return;
    const name=button.dataset.campaignAction;
    switch(name){
      case 'new':
        if((state.hasRun||state.phase==='Recovery')&&!confirm('Replace your saved campaign with a new run? This discards the current run.'))return;
        return mutate('/new');
      case 'delete':
        if(!confirm('Delete this saved campaign? This permanently removes this browser’s run.'))return;
        return mutate('',{},{method:'DELETE'});
      case 'forfeit':
        if(!confirm('Forfeit this match? This records a loss and costs lives. Your run continues if you have lives left.'))return;
        return mutate('/forfeit');
      case 'next-hand':return mutate('/next-hand',{},{live:true});
      case 'continue':case 'start-match':return mutate('/'+name);
      case 'result-continue':case 'victory-continue':case 'shop-leave':case 'shop-reroll':
        return mutate('/'+name.replace('-','/'));
      case 'sell':return mutate('/shop/sell',{jokerId:button.dataset.jokerId});
      case 'buy':case 'pull':return mutate('/shop/'+name,{offerId:button.dataset.offerId});
      case 'buy-use':{
        const offer=state.shop.offers.find(o=>o.id===button.dataset.offerId);
        targets('Buy & use '+CampaignCards.feature(state,offer.category,offer.featureId).name,offer.targetCount,button,targetIds=>mutate('/shop/buy-use',{offerId:offer.id,targetIds}),CampaignCards.feature(state,offer.category,offer.featureId).description);
        return;
      }
      case 'use-item':return useItem(button);
      case 'discard-item':return mutate('/items/discard',{category:button.dataset.category,slot:Number(button.dataset.slot)});
      case 'deck':return CampaignCards.targets({title:'Shared deck',cards:state.deck,count:null,invoke:button});
      case 'arrange':return CampaignCards.order(state.table.players.find(p=>p.seat===0).visibleCards,button,orderedCardIds=>mutate('/arrange-hand',{orderedCardIds},{live:true}));
    }
  }
  function bind(container) {
    // New catalog entries remain readable while their dedicated art is pending.
    container.querySelectorAll('img').forEach(img=>img.addEventListener('error',()=>{img.src='/pixel/back.png';},{once:true}));
    container.querySelectorAll('[data-campaign-action]').forEach(button=>button.addEventListener('click',()=>action(button)));
    container.querySelectorAll('[data-campaign-command]').forEach(button=>button.addEventListener('click',()=>{
      const command=state.table.legalCommands[Number(button.dataset.campaignCommand)];
      mutate('/commands',{command},{live:true});
    }));
    container.querySelectorAll('.campaign-hand button[data-card-id]').forEach(button=>button.addEventListener('click',()=>{
      const commands=state.table.legalCommands.filter(c=>c.cardId===Number(button.dataset.cardId));
      if(commands.length===1)return mutate('/commands',{command:commands[0]},{live:true});
      if(commands.length>1){
        const dialog=CampaignCards.modal('Choose this card’s action','<div class="campaign-actions">'+commands.map((c,index)=>'<button class="button primary" data-choice="'+index+'">'+escapeHtml(CampaignTable.commandLabel(c,state))+'</button>').join('')+'</div><button class="button quiet" data-cancel>Cancel</button>',button);
        dialog.querySelectorAll('[data-choice]').forEach(b=>b.onclick=()=>{dialog.close();mutate('/commands',{command:commands[Number(b.dataset.choice)]},{live:true});});
        dialog.querySelector('[data-cancel]').onclick=()=>dialog.close();
      }
    }));
  }
  document.addEventListener('visibilitychange',()=>{
    stop();
    if(document.hidden){readSerial++;readController?.abort();}else if(active){refresh({quiet:true}).then(schedule);}
  });
  window.addEventListener('pagehide',leave);
  window.addEventListener('pageshow',event=>{if(event.persisted&&root().classList.contains('active'))open();});
  return {open,leave};
})();
