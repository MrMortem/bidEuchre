/* Physical identity and presentation helpers shared by campaign screens. */
window.CampaignCards = (() => {
  const e = value => escapeHtml(value);
  const words = value => splitWords(value);
  const curated = {
    Joker:['ThreesCompany','HighRoller','LoverBoy','SnakeEyes','TwinFlame','Clubhouse','TheUndertaker','RollerCoaster',
      'DejaVu','LastLaugh','VowofSilence','CureAll','TrendSetter','Contrarian','ConsolationPrize','RoyalCouncil',
      'DeepDigger','LuckyDuck','ProtectiveBubble','PennyPincher','Bully','PeoplePleaser','Millionaire','KingMidas',
      'StickyFingers','Medusa','CheeseTouch','OneUp','Cheater','Alpha','SlickWilly','DoubleDown'],
    Tarot:['Chameleon','Grindstone','Prospector','DevilishDeal','Gambler','Traitor','Astronomer','SpringLoaded','TaxCollector'],
    Sigil:['Bear','Chameleon','Duck','Eagle','Fish','Frog','Spider','Vulture'],
    Gumball:['Chaos','HighLow','Alone','Duelist','Poker','PartnersBest','Lowballer','Telepath'],
    Enhancement:['Lucky','Golden','Wild','Cursed','Stone','Silver','Glass','Bouncy']
  };
  function feature(state, category, key) {
    return state.catalog?.find(f => f.category === category && (f.key === key || f.id === key))
      ?? { name:words(key || category), description:'', id:'', key, category };
  }
  function art(feature) {
    if (!feature.id) return '';
    const [category, slug] = feature.id.split(':');
    if (!/^[a-z-]+$/.test(category) || !/^[a-z0-9-]+$/.test(slug)) return '';
    // Card-shaped surfaces show a Sigil as its collector card; playing-card badges keep the square icon.
    if (feature.category === 'Sigil' && curated.Sigil.includes(feature.key)) return collectorArt(feature.key);
    return curated[feature.category]?.includes(feature.key)
      ? '/pixel/curated-' + category + '-' + slug + '.png'
      : '/pixel/campaign-placeholders/' + (category==='gumball'?'planet':category) + '/' + slug + '.png';
  }
  /** Collector card art for a Sigil name, 'Legendary' or 'Pack'. */
  function collectorArt(key) {
    return /^[A-Za-z]+$/.test(key || '') ? '/pixel/curated-collector-' + key.toLowerCase() + '.png' : '';
  }
  function label(card, index = null) {
    const rank = {Nine:'9',Ten:'10',Jack:'Jack',Queen:'Queen',King:'King',Ace:'Ace'}[card.face.rank] || cardRank(card.face);
    return (index == null ? '' : 'Card ' + (index+1) + ': ') + rank + ' of ' + card.face.suit
      + (card.enhancement ? ', ' + words(card.enhancement) + ' enhancement' : '')
      + (card.sigil ? ', ' + words(card.sigil) + ' sigil' : '');
  }
  function html(card, {interactive=false, index=null, selected=false, focusKey='', pokerWinner=false} = {}) {
    const tag = interactive ? 'button' : 'span';
    const enhanced = curated.Enhancement.includes(card.enhancement);
    const background = enhanced ? '/pixel/curated-enhancement-' + card.enhancement.toLowerCase() + '.png' : '/pixel/card-background-white.png';
    const sigil = curated.Sigil.includes(card.sigil) ? '<img class="campaign-sigil" src="/pixel/curated-sigil-' + card.sigil.toLowerCase() + '.png" alt="" aria-hidden="true">' : '';
    return '<' + tag + (interactive ? ' type="button"' : ' role="img"') + ' class="playing-card campaign-card ' + (enhanced?'campaign-card-enhanced ':'') + (interactive?'legal ':'') + (selected?'selected ':'') + (pokerWinner?'campaign-poker-winning-card':'') + '" data-card-id="' + card.id + '"'
      + ' data-focus-key="' + e(focusKey || 'card:'+card.id) + '" aria-label="' + e(label(card,index)+(pokerWinner?', winning Poker hand':'')) + '"' + (interactive ? ' aria-pressed="'+selected+'"' : '')
      + ' style="--card-background:url(&quot;'+background+'&quot;)">' + cardFaceHtml(card.face) + sigil
      + (card.enhancement ? '<span class="campaign-enhancement" aria-hidden="true">'+e(words(card.enhancement))+'</span>' : '') + '</'+tag+'>';
  }
  function modal(title, content, invoke) {
    close(false);
    const dialog = document.createElement('dialog');
    dialog.className = 'campaign-dialog';
    dialog.innerHTML = '<div class="campaign-dialog-heading"><h2 id="campaign-dialog-title" tabindex="-1">'+e(title)+'</h2><button type="button" class="icon-button" data-modal-close aria-label="Close dialog">×</button></div>'+content;
    dialog.querySelector('[data-modal-close]').onclick=()=>dialog.close();
    dialog.setAttribute('aria-labelledby','campaign-dialog-title');
    const restoreKey = invoke?.dataset.focusKey;
    const restore = () => {
      dialog.remove();
      const element = restoreKey ? [...document.querySelectorAll('[data-focus-key]')].find(x=>x.dataset.focusKey===restoreKey) : invoke;
      if (element?.isConnected) element.focus();
    };
    dialog.addEventListener('close',restore,{once:true});
    document.body.append(dialog);
    dialog.showModal();
    return dialog;
  }
  function close(restore = true) {
    const dialog = document.querySelector('.campaign-dialog');
    if (dialog) { if (restore) dialog.close(); else dialog.remove(); }
  }
  function targets({title,cards,count,invoke,onConfirm,description=''}) {
    const chosen = new Set();
    const inspect = count == null;
    const dialog = modal(title,(description?'<p class="campaign-prose">'+e(description)+'</p>':'')+'<p class="campaign-prose">'+(inspect ? 'Your shared deck. Card effects remain between matches.' : count===0 ? 'Confirm to use this item.' : 'Choose exactly '+count+' distinct '+(count===1?'card':'cards')+'.')+'</p>'
      + '<p id="campaign-selection-status" role="status"></p><div class="campaign-deck-grid">'+cards.map((card,index)=>'<div>'+html(card,{interactive:!inspect&&count>0,index})+'<small>'+e(label(card))+'</small></div>').join('')+'</div>'
      + '<div class="campaign-actions"><button class="button quiet" data-cancel>Cancel</button>'+(inspect?'':'<button class="button primary" data-confirm>'+(count===0?'Use item':'Confirm targets')+'</button>')+'</div>',invoke);
    const update = () => {
      dialog.querySelector('#campaign-selection-status').textContent = inspect||count===0 ? '' : chosen.size+' / '+count+' selected';
      dialog.querySelectorAll('button[data-card-id]').forEach(button=>{
        const selected=chosen.has(Number(button.dataset.cardId));
        button.dataset.cardLabel??=button.getAttribute('aria-label');
        button.setAttribute('aria-label',button.dataset.cardLabel+(selected?', target '+([...chosen].indexOf(Number(button.dataset.cardId))+1):''));
        button.setAttribute('aria-pressed',String(selected));
        button.classList.toggle('selected',selected);
        button.querySelector('.campaign-target-number')?.remove();
        if(selected){const badge=document.createElement('span');badge.className='campaign-target-number';badge.textContent='Target '+([...chosen].indexOf(Number(button.dataset.cardId))+1);button.append(badge);}
        button.disabled=!selected && chosen.size>=count;
      });
      const confirm=dialog.querySelector('[data-confirm]');
      if(confirm)confirm.disabled=chosen.size!==count;
    };
    dialog.querySelectorAll('button[data-card-id]').forEach(button=>button.addEventListener('click',()=>{
      const id=Number(button.dataset.cardId);
      if(chosen.has(id))chosen.delete(id);else if(chosen.size<count)chosen.add(id);
      update();
    }));
    dialog.querySelector('[data-cancel]').onclick=()=>dialog.close();
    dialog.querySelector('[data-confirm]')?.addEventListener('click',()=>{
      const ids=[...chosen];dialog.close();onConfirm(ids);
    });
    update();
    dialog.querySelector(inspect?'h2':count===0?'[data-confirm]':'button[data-card-id]')?.focus({preventScroll:true});
    dialog.scrollTop=0;
  }
  function order(cards,invoke,onConfirm) {
    const ordered=[...cards];
    let selected=0;
    const dialog=modal('Arrange your hand','<p class="campaign-prose">Choose a card, then move it left or right. Save the new order when ready.</p><div data-order-cards class="campaign-card-row"></div><p data-order-status role="status"></p><div class="campaign-actions"><button class="button quiet" data-left>Move left</button><button class="button quiet" data-right>Move right</button><button class="button quiet" data-cancel>Cancel</button><button class="button primary" data-save>Save order</button></div>',invoke);
    function render(){
      dialog.querySelector('[data-order-cards]').innerHTML=ordered.map((card,index)=>html(card,{interactive:true,index,selected:index===selected})).join('');
      dialog.querySelectorAll('button[data-card-id]').forEach((button,index)=>button.onclick=()=>{selected=index;render();dialog.querySelectorAll('button[data-card-id]')[selected].focus();});
      dialog.querySelector('[data-left]').disabled=selected===0;
      dialog.querySelector('[data-right]').disabled=selected===ordered.length-1;
      dialog.querySelector('[data-order-status]').textContent=label(ordered[selected])+' at position '+(selected+1)+' of '+ordered.length;
    }
    function move(delta){const next=selected+delta;if(next<0||next>=ordered.length)return;[ordered[selected],ordered[next]]=[ordered[next],ordered[selected]];selected=next;render();}
    dialog.querySelector('[data-left]').onclick=()=>move(-1);
    dialog.querySelector('[data-right]').onclick=()=>move(1);
    dialog.querySelector('[data-cancel]').onclick=()=>dialog.close();
    dialog.querySelector('[data-save]').onclick=()=>{dialog.close();onConfirm(ordered.map(card=>card.id));};
    render();
  }
  return {html,label,feature,art,collectorArt,targets,order,modal,close};
})();
