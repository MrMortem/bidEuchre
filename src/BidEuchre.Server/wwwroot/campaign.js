/* Cookie-scoped campaign controller. Mutations are single-flight and never replayed. */
window.CampaignClient = (() => {
  let active=false,generation=0,state=null,pending=false,loading=false,timer=null,readController=null,readSerial=0,renderKey='',screenKey='';
  let message='',connectionLost=false,lastFeedback='';
  let mutationController=null,mutationFocus=null,shopChoice=null,shopTargets=new Set(),shopSelectionVersion=null,arcadeView=false;
  let pokerTimer=null,pokerKey=null,pokerAttemptVersion=null;
  const root=()=>document.querySelector('#campaign-view');
  const content=()=>document.querySelector('#campaign-content');
  const cap=key=>state?.capabilities?.[key];
  function announce(text,error=false) {
    message=text;
    const status=document.querySelector('#campaign-status');
    status.textContent=text;status.classList.toggle('error',error);
  }
  function stop() {clearTimeout(timer);timer=null;}
  function stopPokerTimer() {clearTimeout(pokerTimer);pokerTimer=null;}
  function leave() {
    active=false;generation++;readSerial++;stop();readController?.abort();readController=null;
    stopPokerTimer();pokerKey=null;pokerAttemptVersion=null;
    mutationFocus=null;mutationController?.abort();
    shopChoice=null;shopTargets=new Set();shopSelectionVersion=null;
    arcadeView=false;CampaignArcadeView.reset();
    CampaignCards.close(false);
    CampaignScreens.backdrop(null);
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
  function pokerCommand() {
    if(state?.phase==='InMatch'&&!state.requiresContinue&&state.table?.phase==='PokerReveal')
      return state.table.legalCommands.find(c=>c.kind==='complete-poker');
    return null;
  }
  function syncPokerReveal() {
    const command=pokerCommand();
    // Versions are run ID + revision. The same showdown can gain a newer version before its timer fires.
    const key=command?state.version.slice(0,state.version.lastIndexOf(':'))+':'+state.table.dealSerial:null;
    if(key!==pokerKey){stopPokerTimer();pokerKey=key;pokerAttemptVersion=null;}
    if(!key||document.hidden||connectionLost){stopPokerTimer();return;}
    if(pokerTimer!==null||pending||loading||pokerAttemptVersion===state.version)return;
    const myGeneration=generation;
    pokerTimer=setTimeout(()=>{
      pokerTimer=null;
      if(!active||generation!==myGeneration||pokerKey!==key||document.hidden||pending||loading||connectionLost)return;
      const currentCommand=pokerCommand();
      if(!currentCommand)return;
      pokerAttemptVersion=state.version;
      mutate('/commands',{command:currentCommand},{live:true});
    },3000);
  }
  async function refresh({quiet=false}={}) {
    if(!active||pending||document.hidden&&quiet)return;
    const myGeneration=generation,serial=++readSerial;
    readController?.abort();const controller=new AbortController();readController=controller;
    const timeout=setTimeout(()=>controller.abort(),8000);
    try{
      const next=await request('','GET',null,controller.signal);
      if(!active||myGeneration!==generation||serial!==readSerial)return;
      if(!quiet)pokerAttemptVersion=null;
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
    if(next.phase!=='Shop')arcadeView=false;
    else if(next.shop?.arcade?.pending)arcadeView=true;
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
    CampaignScreens.backdrop(state);
    root().setAttribute('aria-busy',String(pending||loading));
    const refreshButton=document.querySelector('#campaign-refresh');
    refreshButton.disabled=pending||loading;
    const showArcade=arcadeView&&state?.phase==='Shop'&&!state.shop?.useSession;
    const key=(state?JSON.stringify(showArcade?{...state,feedback:null,notice:null}:state):'loading')+':'+showArcade;
    if(key!==renderKey) {
      const focused=document.activeElement?.dataset.focusKey;
      const detailKey=element=>element.querySelector('summary')?.textContent.split(' · ')[0];
      const openDetails=[...content().querySelectorAll('details[open]')].map(detailKey);
      const scroll=window.scrollY;
      content().innerHTML=state?(showArcade?CampaignArcadeView.render(state):CampaignScreens.render(state)):'<header class="page-header"><div><h1 tabindex="-1">Loading campaign…</h1><p>Your saved progress is being checked.</p></div></header>';
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
      CampaignArcadeView.mount(content(),state);
      renderKey=key;
      const nextScreen=state?.requiresContinue?'continue':state?.shop?.useSession?'shop-use':showArcade?'arcade':state?.table?.phase==='PokerReveal'?'poker-reveal':state?.phase||'loading';
      if(nextScreen!==screenKey){
        content().querySelector('h1')?.focus({preventScroll:nextScreen!=='poker-reveal'});screenKey=nextScreen;
      }else if(focused&&!pending&&!loading) {
        [...root().querySelectorAll('[data-focus-key]')].find(x=>x.dataset.focusKey===focused)?.focus({preventScroll:true});
        window.scrollTo(0,scroll);
        if(document.activeElement===document.body)content().querySelector('h1')?.focus({preventScroll:true});
      }
    }
    root().querySelectorAll('button[data-campaign-action],button[data-campaign-command],button.campaign-card').forEach(button=>{
      if(button.dataset.serverDisabled===undefined)button.dataset.serverDisabled=String(button.disabled);
      button.disabled=button.dataset.serverDisabled==='true'||pending||loading||connectionLost;
    });
    // A response can replace controls while pending. Restore only after their
    // final enabled state is applied, including after an uncertain-outcome read.
    if(!pending&&!loading&&mutationFocus?.generation===generation){
      const desired=mutationFocus;mutationFocus=null;
      const sameScreen=desired.screen===screenKey;
      const control=sameScreen&&desired.key?[...root().querySelectorAll('[data-focus-key]')].find(element=>element.dataset.focusKey===desired.key&&!element.disabled):null;
      const fallback=connectionLost?refreshButton:(!sameScreen?content().querySelector('h1'):content().querySelector('.campaign-moves h2')||content().querySelector('h1'));
      const target=control||fallback;
      if(target){if(!control&&target!==refreshButton)target.tabIndex=-1;target.focus({preventScroll:true});}
    }
    const warning=document.querySelector('#campaign-save-warning');
    warning.hidden=state?.saveStatus!=='failed';
    warning.textContent=state?.saveStatus==='failed'?'Progress is not saved. '+(state.notice||'Changes are retained in this session only.'): '';
    syncPokerReveal();
  }
  async function mutate(route,body={}, {live=false,method='POST'}={}) {
    if(!active||pending||loading||connectionLost||!state)return;
    const myGeneration=generation;
    mutationFocus={generation:myGeneration,key:document.activeElement?.dataset.focusKey,screen:screenKey};
    const controller=new AbortController();mutationController=controller;
    let timedOut=false;
    const deadline=setTimeout(()=>{timedOut=true;controller.abort();},8000);
    const data={expectedVersion:state.version,...body};
    if(live){data.expectedRevision=state.table.revision;data.expectedDealSerial=state.table.dealSerial;}
    pending=true;stop();readSerial++;readController?.abort();CampaignCards.close(false);render();
    let needsRefresh=false;
    try {
      const next=await request(route,method,data,controller.signal);
      if(active&&myGeneration===generation){announce('Saved.');adopt(next);}
    }catch(error) {
      if(active&&myGeneration===generation){
        CampaignCards.close();
        announce(error.status===409?'The table changed; choose again. Refreshing your campaign.':(timedOut?'The action response timed out.':error.message)+(error.status?'':' The request may have been accepted. Refreshing to check progress.'),true);
        needsRefresh=true;
      }
    }finally{
      clearTimeout(deadline);
      if(mutationController===controller)mutationController=null;
      // Keep old choices locked until failure reconciliation returns a fresh version.
      if(needsRefresh&&active&&myGeneration===generation)loading=true;
      pending=false;
      if(active&&myGeneration===generation){render();if(needsRefresh)await refresh({quiet:true});schedule();}
      else if(active){
        announce('An earlier action may have been accepted. Refreshing to check progress.',true);
        await refresh({quiet:true});schedule();
      }
    }
  }
  function targets(title,count,invoke,confirm,description='',cards=[]) {
    CampaignCards.targets({title,cards,count,invoke,onConfirm:confirm,description});
  }
  function useItem(button) {
    if(state.phase!=='InMatch'||state.requiresContinue)return;
    const category=button.dataset.category,slot=Number(button.dataset.slot);
    const item=(category==='Tarot'?state.inventory.tarot:state.inventory.sigils).find(x=>x.slot===slot);
    if(!item)return;
    const itemFeature=CampaignCards.feature(state,category,item.id);
    const name=itemFeature.name;
    targets('Use '+name,item.targetCount,button,targetIds=>{
      mutate('/commands',{command:category==='Tarot'?{kind:'use-tarot',slot,targetIds}:{kind:'use-sigil',slot,targetId:targetIds[0]}},{live:true});
    },itemFeature.description,item.targetCount?state.table?.players.find(p=>p.seat===0)?.visibleCards||[]:[]);
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
      case 'arcade-open':arcadeView=true;return render();
      case 'arcade-close':if(!state.shop?.arcade?.pending){arcadeView=false;render();}return;
      case 'arcade-lane':return CampaignArcadeView.choose('lane',Number(button.dataset.value),content());
      case 'arcade-pocket':return CampaignArcadeView.choose('pocket',Number(button.dataset.value),content());
      case 'arcade-play-plinko':return mutate('/shop/arcade/plinko',{lane:CampaignArcadeView.selection().lane});
      case 'arcade-play-roulette':return mutate('/shop/arcade/roulette',{pocket:CampaignArcadeView.selection().pocket});
      case 'arcade-keep':case 'arcade-use':case 'arcade-dismiss':return mutate('/shop/arcade/'+name.slice(7),{playId:button.dataset.playId});
      case 'arcade-pick':{
        const owned=new Set((state.inventory?.jokers||[]).map(j=>j.id));
        return mutate('/shop/arcade/pick',{playId:button.dataset.playId,index:Number(button.dataset.index)}).then(()=>{
          const granted=(state?.inventory?.jokers||[]).map(j=>j.id).filter(id=>!owned.has(id));
          if(granted.length){CampaignArcadeView.noteGranted(granted.map(id=>CampaignCards.feature(state,'Joker',id).name));renderKey='';render();}
        });
      }
      case 'sell':return mutate('/shop/sell',{jokerId:button.dataset.jokerId});
      case 'buy':case 'pull':return mutate('/shop/'+name,{offerId:button.dataset.offerId});
      case 'print-tickets':return mutate('/shop/print',{offerId:button.dataset.offerId,count:Number(button.dataset.count)});
      case 'keep-ticket':return mutate('/shop/keep-ticket',{offerId:button.dataset.offerId,ticketId:button.dataset.ticketId});
      case 'use-ticket':return mutate('/shop/begin-ticket-use',{offerId:button.dataset.offerId,ticketId:button.dataset.ticketId});
      case 'buy-use':return mutate('/shop/begin-sigil-use',{offerId:button.dataset.offerId});
      case 'select-session-item':return chooseShopItem(button);
      case 'apply-session-item':
        if(!shopChoice||shopTargets.size!==shopChoice.targetCount)return;
        return shopChoice.purchased?mutate('/shop/use-purchased',{targetIds:[...shopTargets]}):mutate('/items/use',{category:'Tarot',slot:shopChoice.slot,targetIds:[...shopTargets]});
      case 'finish-use':return mutate('/shop/finish-use');
      case 'use-item':return useItem(button);
      case 'discard-item':return mutate('/items/discard',{category:button.dataset.category,slot:Number(button.dataset.slot)});
      case 'deck':return CampaignCards.targets({title:'Shared deck',cards:state.deck,count:null,invoke:button});
      case 'arrange':return CampaignCards.order(state.table.players.find(p=>p.seat===0).visibleCards,button,orderedCardIds=>mutate('/arrange-hand',{orderedCardIds},{live:true}));
    }
  }
  function chooseShopItem(button) {
    const session=state.shop?.useSession;
    if(!session)return;
    shopTargets=new Set();
    shopChoice=button.dataset.purchased==='true'&&!session.purchasedItemUsed
      ? {purchased:true,category:session.category,id:session.featureId,targetCount:session.targetCount}
      : button.dataset.slot!==undefined?{...state.inventory.tarot.find(item=>item.slot===Number(button.dataset.slot)),category:'Tarot',purchased:false}:null;
    if(shopChoice?.id===undefined)shopChoice=null;
    updateShopSelection();
  }
  function updateShopSelection() {
    const panel=content().querySelector('[data-shop-use-session]');if(!panel)return;
    const feature=shopChoice?CampaignCards.feature(state,shopChoice.category,shopChoice.id):null;
    panel.querySelector('[data-session-item-name]').textContent=feature?'Use '+feature.name:'Purchased item used';
    panel.querySelector('[data-session-description]').textContent=feature?feature.description:'Use another inventory ticket, or choose Done to return to the shop.';
    panel.querySelector('[data-session-selection-status]').textContent=!shopChoice?'':shopChoice.targetCount===0?'No cards needed. Use this item when ready.':shopTargets.size+' / '+shopChoice.targetCount+' selected';
    panel.querySelectorAll('[data-campaign-action="select-session-item"]').forEach(button=>{
      const selected=!!shopChoice&&(shopChoice.purchased?button.dataset.purchased==='true':button.dataset.slot===String(shopChoice.slot));
      button.setAttribute('aria-pressed',String(selected));
    });
    panel.querySelectorAll('button[data-card-id]').forEach(button=>{
      const id=Number(button.dataset.cardId),selected=shopTargets.has(id),position=[...shopTargets].indexOf(id)+1;
      button.dataset.cardLabel??=button.getAttribute('aria-label');
      button.setAttribute('aria-label',button.dataset.cardLabel+(selected?', target '+position:''));
      button.setAttribute('aria-pressed',String(selected));button.classList.toggle('selected',selected);
      button.querySelector('.campaign-target-number')?.remove();
      if(selected){const badge=document.createElement('span');badge.className='campaign-target-number';badge.textContent='Target '+position;button.append(badge);}
      const disabled=!shopChoice||shopChoice.targetCount===0||!selected&&shopTargets.size>=shopChoice.targetCount;
      button.dataset.serverDisabled=String(disabled);button.disabled=disabled||pending||loading||connectionLost;
    });
    const use=panel.querySelector('[data-campaign-action="apply-session-item"]');
    const disabled=!shopChoice||shopTargets.size!==shopChoice.targetCount;
    use.textContent=feature?'Use '+feature.name:'Use selected item';use.dataset.serverDisabled=String(disabled);use.disabled=disabled||pending||loading||connectionLost;
  }
  function bind(container) {
    const sessionPanel=container.querySelector('[data-shop-use-session]');
    if(sessionPanel){
      // Feedback and save notices may change on an unchanged-version poll.
      // Repaint that response without discarding the player's current choice.
      if(shopSelectionVersion!==state.version){
        shopChoice=null;shopTargets=new Set();shopSelectionVersion=state.version;
        const initial=sessionPanel.querySelector('[data-campaign-action="select-session-item"]:not(:disabled)');
        if(initial)chooseShopItem(initial);else updateShopSelection();
      }else updateShopSelection();
      sessionPanel.querySelectorAll('button[data-card-id]').forEach(button=>button.addEventListener('click',()=>{
        if(pending||loading||connectionLost||!shopChoice)return;
        const id=Number(button.dataset.cardId);
        if(shopTargets.has(id))shopTargets.delete(id);else if(shopTargets.size<shopChoice.targetCount)shopTargets.add(id);
        updateShopSelection();
      }));
    }
    // New catalog entries remain readable while their dedicated art is pending.
    container.querySelectorAll('img').forEach(img=>img.addEventListener('error',()=>{if(img.hasAttribute('data-arcade-art'))img.hidden=true;else img.src='/pixel/back.png';},{once:true}));
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
    if(document.hidden){stopPokerTimer();readSerial++;readController?.abort();}else if(active){refresh({quiet:true}).then(schedule);}
  });
  window.addEventListener('pagehide',leave);
  window.addEventListener('pageshow',event=>{if(event.persisted&&root().classList.contains('active'))open();});
  return {open,leave};
})();
