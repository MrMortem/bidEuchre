/* Arcade outcomes come from the saved campaign. Animation only presents that result. */
window.CampaignArcadeView = (() => {
  let lane=4,pocket=1,granted=null;
  const presented=new Set(),playing=new Set();
  const rowY=[342,393,444,495,545,596,647,698,750,804,856,909,963,1017,1082];
  const e=value=>escapeHtml(value);
  const button=(...args)=>CampaignScreens.button(...args);
  const enabled={enabled:true};
  const wheelAngle=landing=>(360-(landing-1)*30)%360;
  const point=(x,y)=>({left:(100*x/1199)+'%',top:(100*y/1312)+'%'});
  const ballPoint=(position,step)=>point(78+40*position,rowY[step]-(step<14?18:0));
  const binPoint=landing=>point(112+98*landing,1082);
  const nozzlePoint=choice=>point(318+80*choice,290);
  function reset(){for(const animation of playing)animation.cancel();playing.clear();lane=4;pocket=1;granted=null;presented.clear();}
  /** Remember the Jokers a Legendary card just granted; the next render names them once. */
  function noteGranted(names){granted=names&&names.length?names:null;}
  function packCards(result){
    return '<div class="collector-pack" role="group" aria-label="Collector\'s Pack: choose one card">'+result.pack.map((card,index)=>{
      const label=card.title+' — '+card.name;
      return '<button type="button" class="collector-card'+(card.legendary?' legendary':'')+'" data-collector-card data-campaign-action="arcade-pick" data-focus-key="arcade-pick:'+index+'" data-play-id="'+e(result.playId)+'" data-index="'+index+'" aria-label="'+e(label)+'" title="'+e(label)+'"'+(result.pick?.enabled?'':' disabled')+'>'
        +'<img src="'+e(CampaignCards.collectorArt(card.key))+'" alt=""></button>';
    }).join('')+'</div>';
  }
  function tile(state){
    const arcade=state.shop.arcade;
    if(!arcade)return '';
    return '<article class="campaign-feature campaign-arcade-tile"><div class="arcade-sign" aria-hidden="true">✦<br>ARCADE</div><div><p class="eyebrow">Games & prizes</p><h3>The Arcade</h3><p>Plinko and roulette. Earn a Collector\'s Pack, an arcade ticket, or no prize. $'+arcade.price+' per game, once each per visit.</p>'
      +button('arcade-open',arcade.pending?'Resolve Arcade result':'Enter Arcade',enabled,'primary')+'</div></article>';
  }
  function plinko(result){
    const selected=result?.choice??lane,ball=result?binPoint(result.landing):nozzlePoint(selected),dropper=point(318+80*selected,185);
    return '<div class="arcade-plinko" role="img" aria-label="Plinko board. Eleven slots score 6, 5, 4, 3, 2, 1, 2, 3, 4, 5, 6 from left to right.">'
      +'<img class="arcade-board-art" data-arcade-art src="/pixel/arcade-plinko-board.png" alt="">'
      +'<img class="arcade-dropper" data-arcade-dropper data-arcade-art src="/pixel/arcade-plinko-dropper.png" alt="" style="left:'+dropper.left+';top:'+dropper.top+'">'
      +'<span class="arcade-ball" data-arcade-ball style="left:'+ball.left+';top:'+ball.top+'"></span></div>';
  }
  function roulette(result){
    const point=angle=>[200+174*Math.cos(angle*Math.PI/180),200+174*Math.sin(angle*Math.PI/180)];
    let pockets='';
    for(let number=1;number<=12;number++){
      const center=-90+(number-1)*30,start=point(center-15),end=point(center+15),text=[200+140*Math.cos(center*Math.PI/180),200+140*Math.sin(center*Math.PI/180)];
      pockets+='<path d="M200 200 L'+start.join(' ')+' A174 174 0 0 1 '+end.join(' ')+' Z" fill="'+(number%2?'#a12f46':'#192838')+'" stroke="#e4c48b" stroke-width="2"/><text x="'+text[0]+'" y="'+text[1]+'" text-anchor="middle" dominant-baseline="middle">'+number+'</text>';
    }
    return '<div class="arcade-roulette" role="img" aria-label="Twelve-pocket roulette wheel. Odd numbers red; even numbers black."><svg viewBox="0 0 400 400" aria-hidden="true"><circle cx="200" cy="200" r="184" fill="#e4c48b"/>'
      +'<g data-arcade-wheel style="transform:rotate('+wheelAngle(result?.landing??1)+'deg);transform-origin:200px 200px">'+pockets+'</g><circle cx="200" cy="200" r="38" fill="#132238" stroke="#e4c48b" stroke-width="5"/><path d="M185 5 L215 5 L200 39Z" fill="#fff6d6" stroke="#132238" stroke-width="3"/></svg></div>';
  }
  function choices(game,count,start,selected,cap){
    return '<div class="arcade-choices" role="group" aria-label="'+(game==='lane'?'Plinko drop lane':'Roulette number')+'">'+Array.from({length:count},(_,index)=>{
      const value=index+start;
      return button('arcade-'+game,game==='lane'?'Lane '+(value+1):String(value),cap,'quiet','data-value="'+value+'" aria-pressed="'+(value===selected)+'"'+(game==='pocket'?' aria-label="'+value+' '+(value%2?'red':'black')+'"':''));
    }).join('')+'</div>';
  }
  function render(state){
    const arcade=state.shop.arcade,result=arcade.pending;
    let html=CampaignScreens.heading('The Arcade.','$'+arcade.price+' per game · One play of each game per shop visit.')+CampaignScreens.stats(state);
    if(granted){html+='<p class="collector-granted" role="status">LEGENDARY! New Jokers: '+granted.map(e).join(', ')+'</p>';granted=null;}
    if(result&&result.pack){
      return html+'<section class="panel arcade-result" data-arcade-result="'+e(result.playId)+'" data-landing="'+result.landing+'"><h2>'+e(result.game)+' · '+result.score+' points</h2>'
        +(result.game==='Plinko'?plinko(result):roulette(result))
        +'<p class="arcade-result-copy" role="status">'+(result.game==='Plinko'?'Ball landed in slot '+(result.landing+1)+'.':'You chose '+result.choice+'; the wheel landed on '+result.landing+'.')+' You won a Collector\'s Pack!</p>'
        +'<p>Choose one card. A Sigil must go on one of six offered cards — it cannot be kept.'+(result.pack.some(c=>c.legendary)?' The Legendary fills your empty Joker slots with random Jokers.':'')+'</p>'
        +packCards(result)+'</section>';
    }
    if(result){
      const feature=result.prizeCategory?CampaignCards.feature(state,result.prizeCategory,result.featureId):null;
      const art=feature?CampaignCards.art(feature):null;
      html+='<section class="panel arcade-result" data-arcade-result="'+e(result.playId)+'" data-landing="'+result.landing+'"><h2>'+e(result.game)+' · '+result.score+' '+(result.score===1?'point':'points')+'</h2>'
        +(result.game==='Plinko'?plinko(result):roulette(result))
        +'<p class="arcade-result-copy" role="status">'+(result.game==='Plinko'?'Ball landed in slot '+(result.landing+1)+'.':'You chose '+result.choice+'; the wheel landed on '+result.landing+'.')+' '+(feature?'You won '+e(feature.name)+(result.prizeCategory==='Tarot'?' — an arcade ticket.':' — a Sigil.'):'No prize this time.')+'</p>'
        +(art?'<img class="arcade-prize-art" src="'+e(art)+'" alt="'+e(feature.name)+'">':'')
        +'<div class="campaign-actions">'+(feature?button('arcade-keep','Keep '+feature.name,result.keep,'quiet','data-play-id="'+e(result.playId)+'"')+button('arcade-use','Use '+feature.name+' now',result.use,'primary','data-play-id="'+e(result.playId)+'"'):button('arcade-dismiss','Done',result.dismiss,'primary','data-play-id="'+e(result.playId)+'"'))+'</div>'
        +(feature?'<p>Use now opens six random cards. Your held tickets can be used there too. Once opened, you must apply this prize before leaving.</p>':'')+'</section>';
      return html+(feature&&!result.keep.enabled?CampaignScreens.inventory(state):'');
    }
    return html+'<div class="arcade-games"><section class="panel"><h2>Plinko</h2><p>Choose a lane and drop a ball. Score 5–6 for a Collector\'s Pack, 3–4 for a ticket; 1–2 earns no prize.</p>'+plinko()
      +choices('lane',arcade.plinkoLaneCount??8,0,lane,arcade.plinko)+button('arcade-play-plinko','Drop ball · $'+arcade.price,arcade.plinko,'primary')+'</section>'
      +'<section class="panel"><h2>Roulette</h2><p>Pick a number. Exact match: 100 points and a Collector\'s Pack. Matching color: 50 points and a ticket. Otherwise: no prize.</p>'+roulette()
      +choices('pocket',12,1,pocket,arcade.roulette)+button('arcade-play-roulette','Spin wheel · $'+arcade.price,arcade.roulette,'primary')+'</section></div>'
      +button('arcade-close','Back to shop',enabled,'quiet');
  }
  function choose(kind,value,container){
    if(kind==='lane')lane=value;else pocket=value;
    container.querySelectorAll('[data-campaign-action="arcade-'+kind+'"]').forEach(button=>button.setAttribute('aria-pressed',String(Number(button.dataset.value)===value)));
    if(kind==='lane'){
      Object.assign(container.querySelector('[data-arcade-ball]')?.style||{},nozzlePoint(value));
      Object.assign(container.querySelector('[data-arcade-dropper]')?.style||{},point(318+80*value,185));
    }
  }
  function animate(element,frames,options){
    if(typeof element?.animate!=='function')return;
    const animation=element.animate(frames,options),preference=matchMedia('(prefers-reduced-motion: reduce)');
    playing.add(animation);
    const stop=()=>{if(document.hidden||preference.matches||document.body.classList.contains('motion-off'))animation.cancel();};
    const observer=new MutationObserver(stop);
    observer.observe(document.body,{attributes:true,attributeFilter:['class']});
    preference.addEventListener('change',stop);document.addEventListener('visibilitychange',stop);
    const clean=()=>{playing.delete(animation);observer.disconnect();preference.removeEventListener('change',stop);document.removeEventListener('visibilitychange',stop);};
    animation.finished.then(clean,clean);
  }
  function mount(container,state){
    for(const animation of playing)animation.cancel();
    const result=state?.shop?.arcade?.pending;
    const key=state?.version?.split(':')[0]+':'+result?.playId;
    if(!result||!container.querySelector('[data-arcade-result]')||presented.has(key))return;
    presented.add(key);
    try{const saved='bideuchre.arcade-presented:'+key;if(sessionStorage.getItem(saved))return;sessionStorage.setItem(saved,'1');}catch{}
    if(document.hidden||document.body.classList.contains('motion-off')||matchMedia('(prefers-reduced-motion: reduce)').matches)return;
    // Pack cards flip in once, after the ball or wheel settles; a later poll re-renders them face-up.
    container.querySelectorAll('[data-collector-card]').forEach((card,index)=>{card.style.animationDelay=(result.game==='Plinko'?2.9:2.5)+index*.25+'s';card.classList.add('collector-flip');});
    if(result.game==='Plinko')animate(container.querySelector('[data-arcade-ball]'),[nozzlePoint(result.choice),...result.path.map((position,step)=>ballPoint(position,step)),binPoint(result.landing)],{duration:2800,easing:'linear'});
    else animate(container.querySelector('[data-arcade-wheel]'),[{transform:'rotate(0deg)'},{transform:'rotate('+(1440+wheelAngle(result.landing))+'deg)'}],{duration:2400,easing:'cubic-bezier(.12,.6,.18,1)'});
  }
  return {tile,render,choose,mount,reset,noteGranted,wheelAngle,ballPoint,binPoint,selection:()=>({lane,pocket})};
})();
