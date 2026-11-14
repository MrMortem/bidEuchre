const tableMotion = new TableMotion();
const model = {
  motionEnabled: true,
  viewGeneration: 0,
  viewLoading: false,
  connectionLost: false,
  sessionUnavailable: false,
  motionCue: null,
  bidAnnouncement: null,
  engines: [],
  sessions: [],
  session: null,
  sessionId: null,
  viewerSeat: null,
  pollTimer: null,
  pollToken: 0,
  refreshQueue: Promise.resolve(),
  renderMode: null,
  actionPending: false,
  startPending: false,
  rulesLoaded: false,
  rulesGeneration: 0
};

const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];

document.addEventListener('DOMContentLoaded', async () => {
  configureMotion();
  configurePageLifecycle();
  bindNavigation();
  bindForms();
  buildSeatEditor();
  await Promise.all([loadEngines(), loadSessions()]);
});

function bindNavigation() {
  $$('[data-view]').forEach(button => button.addEventListener('click', () => showView(button.dataset.view)));
  $('#new-table-button').addEventListener('click', () => $('#create-panel').classList.remove('hidden'));
  $('#close-create').addEventListener('click', () => $('#create-panel').classList.add('hidden'));
  $('#back-to-tables').addEventListener('click', () => showView('lobby'));
  $('#start-game').addEventListener('click', startCurrentSession);
  $('#retry-rules').addEventListener('click', loadRules);
  $('#retry-connection').addEventListener('click', () => refreshGame({notify:false}));
  $('#viewer-seat').addEventListener('change', event =>
    selectViewerSeat(event.target.value === '' ? null : Number(event.target.value)));
}

function bindForms() {
  $('#create-session-form').addEventListener('submit', createSession);
}

function showView(name) {
  if (name !== 'game') { cancelTableMotion(); model.viewGeneration += 1; }
  document.body.classList.toggle('at-table', name === 'game');
  updateSuitBackdrop(name === 'game' ? model.session?.game?.contract : null);
  $$('.view').forEach(view => view.classList.toggle('active', view.id === `${name}-view`));
  $$('.nav-item').forEach(item => item.classList.toggle('active', item.dataset.view === name));
  if (name !== 'game') stopPolling();
  if (name === 'rules' && !model.rulesLoaded) loadRules();
  if (name === 'lobby') loadSessions();
}

function buildSeatEditor() {
  const defaults = [
    ['You', 'Human'],
    ['TableBot West', 'Bot'],
    ['TableBot North', 'Bot'],
    ['TableBot East', 'Bot']
  ];
  $('#seat-editor').innerHTML = defaults.map(([name, kind], seat) => `
    <div class="seat-row" data-seat="${seat}">
      <span class="seat-number">${seat + 1}</span>
      <label class="field"><span>Name</span><input class="seat-name" value="${name}" maxlength="30"></label>
      <label class="field"><span>Control</span><select class="seat-kind"><option ${kind === 'Human' ? 'selected' : ''}>Human</option><option ${kind === 'Bot' ? 'selected' : ''}>Bot</option></select></label>
      <label class="field engine-field"><span>Engine</span><select class="seat-engine"></select></label>
    </div>`).join('');
  $$('.seat-kind').forEach(select => select.addEventListener('change', updateSeatEditor));
  updateSeatEditor();
}

function updateSeatEditor() {
  $$('.seat-row').forEach(row => {
    const isBot = row.querySelector('.seat-kind').value === 'Bot';
    row.querySelector('.engine-field').classList.toggle('hidden', !isBot);
  });
}

async function api(path, options = {}) {
  // Only reads time out automatically. A submitted move is never retried.
  const read = !options.method || options.method === 'GET';
  const controller = read ? new AbortController() : null;
  const timer = controller ? setTimeout(() => controller.abort(), 8000) : null;
  try {
    const response = await fetch(path, {
      ...options,
      signal:options.signal ?? controller?.signal,
      headers:{ 'Content-Type':'application/json', ...(options.headers || {}) }
    });
    const contentType = response.headers.get('content-type') || '';
    const body = contentType.includes('json') ? await response.json() : await response.text();
    if (!response.ok) {
      const error = new Error(body?.error || body || `Request failed (${response.status})`);
      error.status = response.status;
      throw error;
    }
    return body;
  } finally { if (timer) clearTimeout(timer); }
}

async function loadEngines() {
  try {
    model.engines = await api('/api/engines');
    renderEngines();
    populateEngineSelectors();
  } catch (error) { toast(error.message, true); }
}

function renderEngines() {
  $('#engine-list').innerHTML = model.engines.map(engine => `
    <div class="engine-card">
      <div><strong>${escapeHtml(engine.name)}</strong><small>by ${escapeHtml(engine.author)}</small></div>
      <div>
        <span class="engine-type">${engine.isBuiltIn ? 'Built in' : 'BEUCI process'}</span>
        ${engine.isBuiltIn ? '' : `<button class="button danger remove-engine" data-id="${engine.id}">Remove</button>`}
      </div>
    </div>`).join('');
  $$('.remove-engine').forEach(button => button.addEventListener('click', () => removeEngine(button.dataset.id)));
}

function populateEngineSelectors() {
  const html = model.engines.map(engine => `<option value="${engine.id}">${escapeHtml(engine.name)}</option>`).join('');
  $$('.seat-engine').forEach(select => select.innerHTML = html);
}

async function removeEngine(id) {
  try {
    await api(`/api/engines/${id}`, { method: 'DELETE' });
    await loadEngines();
    toast('Engine removed.');
  } catch (error) { toast(error.message, true); }
}

async function loadSessions() {
  try {
    model.sessions = await api('/api/sessions');
    renderSessions();
  } catch (error) { toast(error.message, true); }
}

function renderSessions() {
  const focused = document.activeElement;
  const focusedId = focused?.classList?.contains('session-open') ? focused.dataset.id : null;
  const list = $('#session-list');
  if (!model.sessions.length) {
    list.innerHTML = '<div class="empty-state"><div><strong>No tables yet</strong>Create a session and choose who takes each seat.</div></div>';
    return;
  }
  list.innerHTML = model.sessions.map(session => `
    <article class="session-card">
      <div class="session-card-top"><h3>${escapeHtml(session.name)}</h3><span class="phase-chip">${splitWords(session.phase)}</span></div>
      <div class="mini-score"><strong>${session.scores[0]}</strong><span>—</span><strong>${session.scores[1]}</strong></div>
      <div class="seat-dots">${session.seats.map(seat => `<span>${seat.kind === 'Bot' ? '⚙' : '●'} ${escapeHtml(seat.name)}</span>`).join('')}</div>
      <button type="button" class="button quiet session-open" data-id="${escapeHtml(session.id)}" aria-label="Open table: ${escapeHtml(session.name)}">Open table</button>
    </article>`).join('');
  const buttons = $$('.session-open');
  buttons.forEach(button => button.addEventListener('click', () => openSession(button.dataset.id)));
  if (focusedId) buttons.find(button => button.dataset.id === focusedId)?.focus();
}

async function createSession(event) {
  event.preventDefault();
  const seats = $$('.seat-row').map(row => ({
    name: row.querySelector('.seat-name').value,
    kind: row.querySelector('.seat-kind').value,
    engineId: row.querySelector('.seat-kind').value === 'Bot' ? row.querySelector('.seat-engine').value : null
  }));
  try {
    const session = await api('/api/sessions', {
      method: 'POST',
      body: JSON.stringify({
        name: $('#table-name').value,
        seats,
        botDelayMilliseconds: Number($('#bot-delay').value)
      })
    });
    model.sessions.unshift(session);
    $('#create-panel').classList.add('hidden');
    await openSession(session.id);
  } catch (error) { toast(error.message, true); }
}

async function openSession(id) {
  beginViewChange();
  tableMotion.reset();
  stopPolling();
  model.sessionId = id;
  model.session = null;
  model.renderMode = null;
  const summary = model.sessions.find(session => session.id === id);
  model.viewerSeat = summary?.seats.find(seat => seat.kind === 'Human')?.seat ?? null;
  showView('game');
  const generation = model.viewGeneration;
  await refreshGame();
  if (generation === model.viewGeneration && !model.sessionUnavailable) {
    $('#back-to-tables').focus?.();
    startPolling();
  }
}

async function startCurrentSession() {
  if (model.startPending || inputLocked()) return;
  const generation = model.viewGeneration;
  const button = $('#start-game');
  model.startPending = true;
  button.disabled = true;
  button.setAttribute('aria-busy', 'true');
  button.textContent = 'Starting bots…';
  try {
    await api(`/api/sessions/${model.sessionId}/start?animateDeal=${model.motionEnabled}`, { method: 'POST', body: '{}' });
    if (generation === model.viewGeneration) await refreshGame();
  } catch (error) { if (generation === model.viewGeneration) toast(error.message, true); }
  finally {
    if (generation !== model.viewGeneration) return;
    model.startPending = false;
    button.disabled = false;
    button.removeAttribute('aria-busy');
    button.textContent = 'Start game';
    syncInputState();
  }
}

function refreshGame({ notify = true } = {}) {
  const sessionId = model.sessionId;
  const viewerSeat = model.viewerSeat;
  const generation = model.viewGeneration;
  const current = () => generation === model.viewGeneration && sessionId === model.sessionId && viewerSeat === model.viewerSeat;
  const operation = async () => {
    if (!sessionId || !current()) return;
    try {
      const query = viewerSeat === null ? '' : `?seat=${viewerSeat}`;
      const session = await api(`/api/sessions/${sessionId}${query}`);
      if (!current()) return;
      if (model.connectionLost) tableMotion.reset();
      model.session = session;
      model.viewLoading = false;
      setConnectionState(false);
      renderGame();
      syncInputState();
    } catch (error) {
      if (!current()) return;
      model.viewLoading = false;
      setConnectionState(true, error.status === 404);
      if (notify && error.status !== 404) toast('Could not refresh the table. Reconnecting…', true);
    }
  };
  const queued = model.refreshQueue.then(operation, operation);
  model.refreshQueue = queued.catch(() => {});
  return queued;
}

function renderGame() {
  const session = model.session;
  if (!session) return;
  $('#game-name').textContent = session.name;
  const humanSeats = session.seats.filter(seat => seat.kind === 'Human');
  const viewerSelect = $('#viewer-seat');
  const viewerKey = JSON.stringify(humanSeats.map(seat => [seat.seat, seat.name]));
  if (viewerSelect.dataset.renderKey !== viewerKey) {
    viewerSelect.innerHTML = humanSeats.length
      ? humanSeats.map(seat => `<option value="${seat.seat}">Seat ${seat.seat + 1}: ${escapeHtml(seat.name)}</option>`).join('')
      : '<option value="">Spectator</option>';
    viewerSelect.dataset.renderKey = viewerKey;
  }
  viewerSelect.value = model.viewerSeat === null ? '' : String(model.viewerSeat);
  viewerSelect.disabled = humanSeats.length === 0;
  const startButton = $('#start-game');
  startButton.classList.toggle('hidden', session.started);
  if (!session.started && !model.startPending) {
    startButton.disabled = false;
    startButton.textContent = 'Start game';
  }

  updateSuitBackdrop(session.game?.contract);
  model.motionCue = tableMotion.update(session.game, `${session.id}:${model.viewerSeat}`);

  if (!session.started || !session.game) {
    resetBoard();
    return;
  }

  const game = session.game;
  $('#team-zero-label').textContent = model.viewerSeat == null ? 'Team 1' : (model.viewerSeat % 2 === 0 ? 'You & partner' : 'Opponents');
  $('#team-one-label').textContent = model.viewerSeat == null ? 'Team 2' : (model.viewerSeat % 2 === 1 ? 'You & partner' : 'Opponents');
  model.renderMode = 'game';
  $('#team-zero-score').textContent = game.scores[0];
  $('#team-one-score').textContent = game.scores[1];
  $('#phase-label').textContent = `${session.dealing ? 'Dealing' : splitWords(game.phase)} · Hand ${game.handNumber}`;
  $('#contract-label').textContent = contractName(game);
  renderBidDock(game);
  $('#dealer-label').textContent = `Seat ${game.dealer + 1} deals · ${game.tricksByTeam[0]}–${game.tricksByTeam[1]} tricks`;
  $('#trick-count').textContent = `${game.completedTricks.length} / 6 tricks`;
  $('#game-error').textContent = session.error || '';
  $('#game-error').classList.toggle('hidden', !session.error);

  if (game.phase !== 'Bidding') cancelDeal();
  game.players.forEach(player => renderPlayer(player, game));
  if (session.dealing && !model.dealAnimation) {
    if (model.dealCompletedHand === `${session.id}:${game.handNumber}`) finishServerDeal(session.id,game.handNumber);
    else startDeal(game);
  } else if (model.motionCue.deal && game.phase === 'Bidding' && !model.dealAnimation) startDeal(game);
  renderTrick(game);
  renderActions(game);
  animateTableCues();
  if (model.motionCue.highBidChanged) announceHighestBid(game);
  syncInputState();
  const eventList = $('#event-list');
  const eventKey = JSON.stringify(game.events);
  if (eventList.dataset.renderKey !== eventKey) {
    eventList.innerHTML = [...game.events].reverse().map(event => `<li>${escapeHtml(event)}</li>`).join('');
    eventList.dataset.renderKey = eventKey;
  }
}

function resetBoard() {
  if (model.renderMode === `waiting:${model.session?.id}`) return;
  model.renderMode = `waiting:${model.session?.id}`;
  $('#team-zero-score').textContent = '0';
  $('#team-one-score').textContent = '0';
  $('#phase-label').textContent = 'Waiting to start';
  $('#contract-label').textContent = 'No contract';
  cancelBidAnnouncement();
  $('#bid-dock').classList.add('hidden');
  $('#contract-label').classList.remove('hidden');
  $('#dealer-label').textContent = 'Dealer not chosen';
  model.session.seats.forEach(seat => {
    const position = $(`#seat-${seat.seat}`);
    position.removeAttribute('data-render-key');
    position.classList.remove('sitting-out');
    position.innerHTML = `<div class="player-tag">${portraitHtml(seat.seat)}<span>${escapeHtml(seat.name)}</span><span>${seat.kind === 'Bot' ? '⚙ Bot' : '● Human'}</span></div><div class="card-row">${backs(6)}</div>`;
  });
  $('#trick-area').innerHTML = '<span class="empty-trick">Cards played here</span>';
  $('#trick-area').removeAttribute('data-render-key');
  $('#action-title').textContent = 'Waiting for the game';
  $('#action-help').textContent = 'Start the session when every seat is ready.';
  $('#action-buttons').innerHTML = '';
  $('#action-buttons').removeAttribute('data-render-key');
  $('#event-list').innerHTML = '';
  $('#event-list').removeAttribute('data-render-key');
}

function renderPlayer(player, game) {
  const seatConfig = model.session.seats.find(seat => seat.seat === player.seat);
  const current = game.currentSeat === player.seat;
  const legalCodes = new Set(current ? game.legalActions.cards.map(cardCode) : []);
  const renderKey = JSON.stringify([
    seatConfig?.kind,
    player.name,
    player.team,
    player.cardCount,
    player.isSittingOut,
    current,
    game.dealer,
    game.phase,
    game.tricksByTeam[player.team],
    player.cards?.map(card => [cardCode(card), card.rank, card.suit]) ?? null,
    [...legalCodes]
  ]);
  const position = $(`#seat-${player.seat}`);
  if (position.dataset.renderKey === renderKey) return;

  const cards = player.cards
    ? player.cards.map(card => cardHtml(card, legalCodes.has(cardCode(card)))).join('')
    : backs(player.cardCount);
  position.classList.toggle('sitting-out', player.isSittingOut);
  position.innerHTML = `
    <div class="player-tag ${current ? 'current' : ''}">
      ${portraitHtml(player.seat)}
      <span class="player-name">${seatConfig?.kind === 'Bot' ? '⚙' : '●'} ${escapeHtml(player.name)}</span>
      ${game.dealer === player.seat ? '<span class="dealer">D</span>' : ''}
      ${player.isSittingOut ? '<span>Sitting out</span>' : ''}
      <span class="tricks">Team · ${game.tricksByTeam[player.team]}</span>
    </div>
    <div class="card-row">${cards}</div>`;
  position.dataset.renderKey = renderKey;
  registerDealHand(position, player.seat);
  if (player.cards && current) {
    position.querySelectorAll('.playing-card.legal').forEach(button =>
      button.addEventListener('click', () => playOrExchange(button.dataset.card)));
  }
}

function renderTrick(game) {
  const area = $('#trick-area');
  let plays = game.currentTrick;
  let completed = null;
  if (!plays.length && game.completedTricks.length) {
    completed = game.completedTricks[game.completedTricks.length - 1];
    plays = completed.plays;
  }
  if (!plays.length) {
    const renderKey = 'empty';
    if (area.dataset.renderKey !== renderKey) {
      area.innerHTML = '<span class="empty-trick">Cards played here</span>';
      area.dataset.renderKey = renderKey;
    }
    return;
  }
  const renderKey = JSON.stringify([
    model.sessionId, game.handNumber,
    plays.map(play => [play.seat, cardCode(play.card)]),
    completed?.number ?? null,
    completed?.winner ?? null
  ]);
  if (area.dataset.renderKey === renderKey) return;
  // Keep existing card nodes: the next play must not restart a card in flight.
  const keys = new Set(plays.map(play => `${play.seat}:${cardCode(play.card)}`));
  [...area.children].forEach(node => { if (!keys.has(node.dataset.playKey)) node.remove(); });
  plays.forEach(play => {
    const key = `${play.seat}:${cardCode(play.card)}`;
    let node = [...area.children].find(child => child.dataset.playKey === key);
    if (!node) {
      node = document.createElement('div');
      node.className = `trick-card seat-${play.seat}`;
      node.dataset.playKey = key;
      node.innerHTML = cardHtml(play.card, false);
      area.append(node);
      if (model.motionCue?.addedPlays.has(key)) {
        const [x, y] = [[0,85],[-95,0],[0,-85],[95,0]][play.seat];
        runMotion(node, [
          { translate:`${x}px ${y}px`, rotate:`${play.seat % 2 ? 8 : -6}deg`, opacity:.5 },
          { translate:`${-x * .035}px ${-y * .035}px`, rotate:'0deg', opacity:1, offset:.8 },
          { translate:'0 0', rotate:'0deg', opacity:1 }
        ], { duration:300 });
      }
    }
    node.classList.toggle('winning-card', completed?.winner === play.seat);
  });
  if (completed) {
    const result = document.createElement('span');
    result.className = 'trick-result';
    result.textContent = `${game.players[completed.winner].name} won trick ${completed.number}`;
    area.append(result);
    if (model.motionCue?.winnerSeat != null) runMotion(result,
      [{ translate:'0 6px', opacity:0 }, { translate:'0 0', opacity:1 }], { duration:220 });
  }
  area.dataset.renderKey = renderKey;
}

function renderActions(game) {
  const title = $('#action-title');
  const help = $('#action-help');
  const actions = $('#action-buttons');
  const buttons = [];
  let titleText;
  let helpText;

  if (model.session?.dealing || model.dealAnimation) {
    titleText = 'Dealing cards';
    helpText = 'Bidding begins when everyone has their six cards.';
  } else if (game.phase === 'HandComplete') {
    titleText = 'Hand complete';
    helpText = 'Review the score, then deal the next hand when everyone is ready.';
    buttons.push({ key: 'next-hand', label: 'Deal next hand', handler: nextHand });
  } else if (game.phase === 'GameComplete') {
    titleText = `Team ${game.gameWinner + 1} wins`;
    helpText = 'The race to 40 is complete. Return to Tables to begin another game.';
  } else if (game.currentSeat !== model.viewerSeat) {
    const current = game.players.find(player => player.seat === game.currentSeat);
    const currentSeat = model.session.seats.find(seat => seat.seat === game.currentSeat);
    if (!current) {
      titleText = splitWords(game.phase);
      helpText = 'The table is moving to the next turn.';
    } else if (currentSeat?.kind === 'Human') {
      titleText = `Waiting for ${current.name}`;
      helpText = `This hot-seat turn belongs to Seat ${current.seat + 1}. Switch hands to reveal that player's legal choices.`;
      buttons.push({
        key: `view:${current.seat}`,
        label: `View ${current.name}'s hand`,
        handler: () => selectViewerSeat(current.seat)
      });
    } else {
      titleText = `${current.name} is thinking`;
      helpText = 'The bot is taking its turn. This panel and the table update automatically.';
    }
  } else if (game.phase === 'Bidding') {
    titleText = 'Make your bid';
    helpText = 'Choose a legal raise, or pass. Contract type stays hidden until the auction ends.';
    game.legalActions.bids.forEach(bid => buttons.push({
      key: `bid:${bid}`,
      label: bidName(bid),
      handler: () => sendAction({ type: 'bid', bid })
    }));
    if (game.legalActions.canPass) {
      buttons.push({ key: 'pass', label: 'Pass', handler: () => sendAction({ type: 'pass' }), quiet: true });
    }
  } else if (game.phase === 'ChoosingContract') {
    titleText = 'Choose the contract';
    helpText = 'Select High, Low, or reveal a trump suit.';
    if (game.legalActions.contractModes.includes('High')) {
      buttons.push({ key: 'contract:high', label: 'High', handler: () => sendAction({ type: 'contract', mode: 'High' }) });
    }
    if (game.legalActions.contractModes.includes('Low')) {
      buttons.push({ key: 'contract:low', label: 'Low', handler: () => sendAction({ type: 'contract', mode: 'Low' }) });
    }
    if (game.legalActions.contractModes.includes('Trump')) {
      game.legalActions.trumpSuits.forEach(suit => buttons.push({
        key: `contract:trump:${suit}`,
        label: `${suitSymbol(suit)} ${suit}`,
        handler: () => sendAction({ type: 'contract', mode: 'Trump', suit })
      }));
    }
  } else if (game.phase.startsWith('Exchanging')) {
    titleText = game.phase === 'ExchangingBidderCard' ? 'Give one card' : 'Return one card';
    helpText = 'Choose a gold-outlined card from your hand on the table. Only your partner sees the private exchange.';
  } else if (game.phase === 'Playing') {
    titleText = 'Play a card';
    helpText = 'Choose a gold-outlined card from your hand. You must follow the effective suit.';
  } else {
    titleText = splitWords(game.phase);
    helpText = 'The table will update when the next action is available.';
  }

  title.textContent = titleText;
  help.textContent = helpText;
  const renderKey = JSON.stringify([titleText, helpText, buttons.map(button => button.key)]);
  if (actions.dataset.renderKey === renderKey) return;
  actions.replaceChildren();
  buttons.forEach(button => addActionButton(button.label, button.handler, button.quiet));
  actions.dataset.renderKey = renderKey;
  setActionBusy(model.actionPending);
}

function addActionButton(label, handler, quiet = false) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = `action-button${quiet ? ' quiet' : ''}`;
  button.textContent = label;
  button.addEventListener('click', handler);
  $('#action-buttons').append(button);
}

async function submitGameRequest(path, body) {
  if (inputLocked()) return;
  const generation = model.viewGeneration;
  setActionBusy(true);
  try {
    await api(path, {method:'POST', body:JSON.stringify(body)});
    if (generation === model.viewGeneration) await refreshGame();
  } catch (error) {
    if (generation === model.viewGeneration) {
      toast(error.message, true);
      // A lost response can still mean the server accepted the move. Read the
      // authoritative state before releasing input; never repeat the POST.
      await refreshGame({notify:false});
    }
  } finally {
    if (generation === model.viewGeneration) setActionBusy(false);
  }
}

async function sendAction(action) {
  return submitGameRequest(`/api/sessions/${model.sessionId}/actions`, {seat:model.viewerSeat, ...action});
}

function playOrExchange(card) {
  const phase = model.session?.game?.phase;
  if (phase !== 'Playing' && !phase?.startsWith('Exchanging')) return;
  sendAction({ type: phase === 'Playing' ? 'play' : 'exchange', card });
}

async function nextHand() {
  return submitGameRequest(`/api/sessions/${model.sessionId}/next-hand?animateDeal=${model.motionEnabled}`, {});
}

async function selectViewerSeat(seat) {
  beginViewChange();
  model.viewerSeat = seat;
  $('#viewer-seat').value = seat === null ? '' : String(seat);
  await refreshGame();
}

function inputLocked() { return model.actionPending || model.viewLoading || model.connectionLost || !!model.dealAnimation || !!model.session?.dealing; }

function syncInputState() {
  const locked = inputLocked();
  const actions = $('#action-buttons');
  actions.setAttribute('aria-busy', String(model.actionPending || model.viewLoading));
  actions.querySelectorAll('button').forEach(button => button.disabled = locked);
  $$('.playing-card.legal').forEach(button => button.disabled = locked);
  $('#start-game').disabled = model.startPending || locked;
  $('#viewer-seat').disabled = model.viewLoading || model.actionPending || !model.session?.seats.some(seat => seat.kind === 'Human');
}

function setActionBusy(busy) { model.actionPending = busy; syncInputState(); }

function beginViewChange() {
  cancelTableMotion();
  model.viewGeneration += 1;
  model.viewLoading = true;
  model.actionPending = false;
  model.startPending = false;
  setConnectionState(false);
  $$('.player-position .card-row').forEach(row => {
    if (row.querySelector('.playing-card')) {
      row.replaceChildren();
      row.parentElement.removeAttribute('data-render-key');
    }
  });
  $('#action-title').textContent = 'Loading hand…';
  $('#action-help').textContent = 'Your hand will appear when the table is ready.';
  syncInputState();
}

function setConnectionState(lost, unavailable = false) {
  if (lost && !model.connectionLost) { cancelTableMotion(); tableMotion.reset(); }
  model.connectionLost = lost;
  model.sessionUnavailable = unavailable;
  $('#connection-status').classList.toggle('hidden', !lost);
  const message = unavailable ? 'This table is no longer available. Return to Tables to start a new game.'
    : 'Connection lost. Reconnecting… Your last table state is shown.';
  if ($('#connection-message').textContent !== message) $('#connection-message').textContent = message;
  $('#retry-connection').classList.toggle('hidden', unavailable);
  if (unavailable) stopPolling();
  syncInputState();
}

async function loadRules() {
  const generation = ++model.rulesGeneration;
  const retry = $('#retry-rules');
  retry.classList.add('hidden');
  try {
    const response = await fetch('/api/rules');
    if (!response.ok) throw new Error('Could not load the rules. Try again.');
    const markdown = await response.text();
    if (generation !== model.rulesGeneration) return;
    $('#rules-content').innerHTML = renderMarkdown(markdown);
    model.rulesLoaded = true;
  } catch {
    if (generation !== model.rulesGeneration) return;
    model.rulesLoaded = false;
    $('#rules-content').textContent = 'Could not load the rules. Check the connection and try again.';
    retry.classList.remove('hidden');
  }
}

function renderMarkdown(markdown) {
  let inCode = false;
  let inList = false;
  const output = [];
  for (const raw of markdown.split('\n')) {
    const line = escapeHtml(raw);
    if (line.startsWith('```')) {
      if (inList) { output.push('</ul>'); inList = false; }
      output.push(inCode ? '</code></pre>' : '<pre><code>');
      inCode = !inCode;
    } else if (inCode) output.push(`${line}\n`);
    else if (/^#{1,3} /.test(line)) {
      if (inList) { output.push('</ul>'); inList = false; }
      const count = line.match(/^#+/)[0].length;
      output.push(`<h${count}>${inlineMarkdown(line.slice(count + 1))}</h${count}>`);
    } else if (/^- /.test(line)) {
      if (!inList) { output.push('<ul>'); inList = true; }
      output.push(`<li>${inlineMarkdown(line.slice(2))}</li>`);
    } else if (/^\d+\. /.test(line)) {
      if (inList) { output.push('</ul>'); inList = false; }
      output.push(`<p>${inlineMarkdown(line)}</p>`);
    } else if (line.trim()) {
      if (inList) { output.push('</ul>'); inList = false; }
      output.push(`<p>${inlineMarkdown(line)}</p>`);
    }
  }
  if (inList) output.push('</ul>');
  return output.join('');
}

function inlineMarkdown(value) {
  return value.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>').replace(/`(.*?)`/g, '<code>$1</code>');
}

function cardHtml(card, legal) {
  const red = card.suit === 'Hearts' || card.suit === 'Diamonds';
  const rank = cardRank(card);
  const code = cardCode(card);
  const label = `${rank} of ${card.suit}`;
  const column = '9TJQKA'.indexOf(code[0]);
  const row = 'CDHS'.indexOf(code[1]);
  return `<button type="button" class="playing-card ${red ? 'red' : ''} ${legal ? 'legal' : ''}" aria-label="${escapeHtml(label)}" data-card="${escapeHtml(code)}" ${legal ? '' : 'disabled'}><span class="card-face-sprite" style="--card-column:${column};--card-row:${row}" aria-hidden="true"></span></button>`;
}

function portraitHtml(seat) {
  return `<span class="portrait portrait-${seat}" aria-hidden="true"></span>`;
}

function backs(count) { return Array.from({ length: count }, () => '<span class="card-back"></span>').join(''); }
function cardRank(card) {
  const rank = String(card?.rank ?? '').trim().toLowerCase();
  const ranks = {
    nine: '9', n: '9', '9': '9',
    ten: '10', t: '10', '10': '10',
    jack: 'J', j: 'J', '11': 'J',
    queen: 'Q', q: 'Q', '12': 'Q',
    king: 'K', k: 'K', '13': 'K',
    ace: 'A', a: 'A', '14': 'A'
  };
  if (ranks[rank]) return ranks[rank];

  const code = String(card?.code ?? '').trim().toUpperCase();
  if (code.startsWith('10')) return '10';
  return ({ N: '9', '9': '9', T: '10', J: 'J', Q: 'Q', K: 'K', A: 'A' })[code[0]] ?? '?';
}

function cardCode(card) {
  const supplied = String(card?.code ?? '').trim().toUpperCase();
  if (/^[9TJQKA][CDHS]$/.test(supplied)) return supplied;

  const rank = ({ '9': '9', '10': 'T', J: 'J', Q: 'Q', K: 'K', A: 'A' })[cardRank(card)];
  const suit = ({ Clubs: 'C', Diamonds: 'D', Hearts: 'H', Spades: 'S' })[card?.suit];
  return rank && suit ? `${rank}${suit}` : supplied;
}
function suitSymbol(suit) { return ({ Clubs: '♣', Diamonds: '♦', Hearts: '♥', Spades: '♠' })[suit] || suit; }
function bidName(bid) { return ({ Three: '3', Four: '4', Five: '5', Six: '6', PartnersBest: 'Partners Best', Alone: 'Alone' })[bid] || bid; }
function splitWords(value) { return String(value || '').replace(/([a-z])([A-Z])/g, '$1 $2'); }
function contractName(game) {
  if (!game.contract) return game.highBid ? `${bidName(game.highBid)} high bid` : 'Auction open';
  const prefix = bidName(game.contract.bid);
  return game.contract.mode === 'Trump' ? `${prefix} · ${suitSymbol(game.contract.trump)} ${game.contract.trump}` : `${prefix} · ${game.contract.mode}`;
}

function startPolling() {
  stopPolling();
  const token = model.pollToken;
  const poll = async () => {
    await refreshGame({ notify: false });
    if (token !== model.pollToken || model.sessionUnavailable || document.hidden) return;
    model.pollTimer = window.setTimeout(poll, document.hidden ? 1500 : 500);
  };
  model.pollTimer = window.setTimeout(poll, 500);
}

function stopPolling() {
  model.pollToken += 1;
  if (model.pollTimer) window.clearTimeout(model.pollTimer);
  model.pollTimer = null;
}
function escapeHtml(value) { return String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[character]); }
function toast(message, error = false) {
  const element = $('#toast');
  element.textContent = message;
  element.className = `toast show ${error ? 'error' : ''}`;
  clearTimeout(element.timer);
  element.timer = setTimeout(() => element.className = 'toast', 3500);
}


function configureMotion() {
  const preference = matchMedia('(prefers-reduced-motion: reduce)');
  const button = $('#motion-toggle');
  let requested = true;
  try { requested = localStorage.getItem('bideuchre.motion') !== 'off'; } catch { }
  const update = () => {
    const supported = typeof button.animate === 'function';
    model.motionEnabled = requested && !preference.matches && supported;
    document.body.classList.toggle('motion-off', !model.motionEnabled);
    button.textContent = `Motion: ${model.motionEnabled ? 'On' : 'Off'}`;
    button.setAttribute('aria-pressed', String(model.motionEnabled));
    button.disabled = preference.matches || !supported;
    button.title = preference.matches ? 'Your system reduced-motion setting is enabled.' : 'Turn table animations on or off.';
    if (!model.motionEnabled) cancelTableMotion();
  };
  update();
  button.addEventListener('click', () => {
    requested = !requested;
    try { localStorage.setItem('bideuchre.motion', requested ? 'on' : 'off'); } catch { }
    update();
  });
  preference.addEventListener('change', update);
  window.addEventListener('resize', cancelBidAnnouncement);
  window.addEventListener('resize', cancelDeal);
  window.addEventListener('resize', () => updateSuitBackdrop(document.body.classList.contains('at-table') ? model.session?.game?.contract : null));
  document.addEventListener('scroll', cancelBidAnnouncement, {capture:true,passive:true});
  document.addEventListener('scroll', cancelDeal, {capture:true,passive:true});
}

function configurePageLifecycle() {
  document.addEventListener('visibilitychange', async () => {
    cancelTableMotion();
    tableMotion.reset();
    stopPolling();
    if (document.hidden || !$('#game-view').classList.contains('active')) return;
    beginViewChange();
    const generation = model.viewGeneration;
    await refreshGame({notify:false});
    if (generation === model.viewGeneration && !document.hidden && !model.sessionUnavailable) startPolling();
  });
}

function runMotion(element, keyframes, options = {}) {
  if (!element || document.hidden || !model.motionEnabled || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  element.getAnimations().filter(animation => animation.animationName !== 'hand-float').forEach(animation => animation.cancel());
  return element.animate(keyframes, { duration:260, easing:'cubic-bezier(.2,.75,.25,1)', ...options });
}

function animateTableCues() {
  const cue = model.motionCue;
  if (!cue) return;
  if (cue.turnSeat != null) runMotion($(`#seat-${cue.turnSeat} .player-tag`),
    [{ translate:'0 0' }, { translate:'0 -4px', offset:.45 }, { translate:'0 0' }], { duration:260 });
  if (cue.winnerSeat != null) {
    runMotion($(`#seat-${cue.winnerSeat} .portrait`),
      [{ scale:'1' }, { scale:'1.12', offset:.45 }, { scale:'1' }], { duration:380 });
    const winner = $('#trick-area .winning-card .playing-card');
    runMotion(winner, [{ scale:'1' }, { scale:'1.065', offset:.45 }, { scale:'1' }], { duration:380 });
  }
  for (const team of cue.scoreTeams) runMotion($(team === 0 ? '#team-zero-score' : '#team-one-score'),
    [{ scale:'1' }, { scale:'1.2', color:'#ffdb62', offset:.4 }, { scale:'1' }], { duration:420 });
}

function cancelTableMotion() {
  cancelDeal();
  cancelBidAnnouncement();
  $('#game-view')?.getAnimations({ subtree:true }).filter(animation => animation.animationName !== 'hand-float').forEach(animation => animation.cancel());
}


function bidBadgeHtml(game) {
  const player = game.players.find(player => player.seat === game.bidder);
  return `<span class="bid-caption">Highest bid</span><strong class="bid-value">${escapeHtml(bidName(game.highBid))}</strong><span class="bid-player">${escapeHtml(player?.name ?? `Seat ${game.bidder + 1}`)}</span>`;
}

function renderBidDock(game) {
  const dock = $('#bid-dock');
  const visible = game.highBid != null && game.bidder != null && ['Bidding','ChoosingContract'].includes(game.phase);
  dock.classList.toggle('hidden', !visible);
  $('#contract-label').classList.toggle('hidden', visible);
  if (!visible) { cancelBidAnnouncement(); delete dock.dataset.bidKey; return; }
  const key = `${model.sessionId}:${game.handNumber}:${game.bidder}:${game.highBid}`;
  if (dock.dataset.bidKey !== key) {
    dock.innerHTML = bidBadgeHtml(game);
    dock.dataset.bidKey = key;
  }
}

function bidFlightGeometry(table, dock, width, height, viewportWidth, viewportHeight) {
  const clamp = (value, size, viewport) => Math.max(size / 2 + 12, Math.min(viewport - size / 2 - 12, value));
  const naturalY = table.top + table.height * .48;
  const x = clamp(table.left + table.width / 2, width, viewportWidth);
  const y = clamp(naturalY < 0 || naturalY > viewportHeight ? viewportHeight / 2 : naturalY, height, viewportHeight);
  const dockVisible = dock.top >= 0 && dock.top + dock.height <= viewportHeight;
  return {x, y, targetX:dockVisible ? dock.left + dock.width / 2 : -width / 2 - 16,
    targetY:dockVisible ? dock.top + dock.height / 2 : y, scale:Math.min(1, dock.width / width)};
}

function announceHighestBid(game) {
  cancelBidAnnouncement();
  if (!model.motionEnabled || document.hidden || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  const dock = $('#bid-dock');
  if (dock.classList.contains('hidden')) return;
  const table = $('.felt-table').getBoundingClientRect();
  const destination = dock.getBoundingClientRect();
  const overlay = document.createElement('div');
  overlay.className = 'bid-badge bid-announcement';
  overlay.setAttribute('aria-hidden','true');
  overlay.innerHTML = bidBadgeHtml(game);
  overlay.style.left = `${table.left + table.width / 2}px`;
  overlay.style.top = `${table.top + table.height * .48}px`;
  $('#game-view').append(overlay);
  const bounds = overlay.getBoundingClientRect();
  const flight = bidFlightGeometry(table, destination, bounds.width, bounds.height, window.innerWidth, window.innerHeight);
  overlay.style.left = `${flight.x - bounds.width / 2}px`;
  overlay.style.top = `${flight.y - bounds.height / 2}px`;
  const x = flight.targetX - flight.x;
  const y = flight.targetY - flight.y;
  const scale = flight.scale;
  const animation = runMotion(overlay, [
    {opacity:0, scale:'.85', translate:'0 10px', offset:0},
    {opacity:1, scale:'1.06', translate:'0 0', offset:.13},
    {opacity:1, scale:'1', translate:'0 0', offset:.25},
    {opacity:1, scale:'1', translate:'0 0', offset:.5},
    {opacity:1, scale:String(scale), translate:`${x}px ${y}px`, offset:.97},
    {opacity:0, scale:String(scale), translate:`${x}px ${y}px`, offset:1}
  ], {duration:800});
  if (!animation) { overlay.remove(); return; }
  const active = {element:overlay,animation};
  model.bidAnnouncement = active;
  dock.classList.add('bid-in-flight');
  const finish = () => {
    overlay.remove();
    if (model.bidAnnouncement === active) {
      model.bidAnnouncement = null;
      dock.classList.remove('bid-in-flight');
    }
  };
  animation.finished.then(finish,finish);
}

function cancelBidAnnouncement() {
  const active = model.bidAnnouncement;
  model.bidAnnouncement = null;
  active?.animation.cancel();
  active?.element.remove();
  $('#bid-dock')?.classList.remove('bid-in-flight');
}

// All suit artwork shares the same 1672 x 941 cover canvas as the flames.
function suitBackdropLayout(width, height, contract) {
  const scale = Math.max(width / 1672, height / 941);
  const originX = (width - 1672 * scale) / 2;
  const originY = (height - 941 * scale) / 2;
  const inset = Math.min(24, Math.min(width, height) * .04);
  return [
    ['Hearts',59,38,157,155], ['Clubs',1439,29,190,177],
    ['Spades',50,698,173,182], ['Diamonds',1458,695,155,191]
  ].map(([suit,x,y,w,h],index) => {
    const left = originX + x * scale, top = originY + y * scale;
    const cardWidth = w * scale, cardHeight = h * scale;
    const active = contract?.mode === 'High' || (contract?.mode === 'Trump' && contract.trump === suit);
    const right = index === 1 || index === 3, bottom = index >= 2;
    let shiftX = 0, shiftY = 0;
    if (contract?.mode === 'Low') {
      shiftX = (right ? width + cardWidth + inset : -cardWidth * 2 - inset) - left;
    } else if (active) {
      shiftX = (right ? Math.min(left - 32, width - cardWidth - inset) : Math.max(left + 32, inset)) - left;
      shiftY = (bottom ? Math.min(top - 20, height - cardHeight - inset) : Math.max(top + 20, inset)) - top;
    }
    return {left,top,width:cardWidth,height:cardHeight,x:shiftX,y:shiftY,active};
  });
}

function updateSuitBackdrop(contract) {
  const layers = $$('.table-scenery > span');
  if (!layers.length) return;
  const layout = suitBackdropLayout(window.innerWidth, window.innerHeight, contract);
  layers.forEach((layer,index) => {
    const position = layout[index];
    layer.style.setProperty('--suit-x', `${position.x}px`);
    layer.style.setProperty('--suit-y', `${position.y}px`);
    layer.classList.toggle('contract-active', position.active);
  });
}

function dealPackets(dealer) {
  return Array.from({length:8},(_,packet) => ({
    seat:(dealer+packet%4+1)%4, firstCard:Math.floor(packet/4)*3,
    count:3, delay:180+packet*250
  }));
}

function registerDealHand(position,seat) {
  const deal = model.dealAnimation;
  if (!deal) return;
  position.querySelectorAll('.card-row > *').forEach((card,index) => {
    const key = `${seat}:${index}`;
    deal.targets.set(key,card);
    card.style.opacity = deal.landed.has(key) ? '' : '0';
  });
}

function cancelDeal() {
  const deal = model.dealAnimation;
  if (!deal) return;
  model.dealAnimation = null;
  deal.animations.forEach(animation => animation.cancel());
  deal.visuals.forEach(visual => visual.remove());
  deal.targets.forEach(card => card.style.opacity = '');
  if (deal.sessionId) finishServerDeal(deal.sessionId,deal.handNumber);
  syncInputState();
}

function startDeal(game) {
  cancelDeal();
  if (!model.motionEnabled || document.hidden || matchMedia('(prefers-reduced-motion: reduce)').matches) {
    if (model.session?.dealing) finishServerDeal(model.sessionId,game.handNumber);
    return;
  }
  const table = $('.felt-table')?.getBoundingClientRect();
  const dealer = $(`#seat-${game.dealer} .player-tag`)?.getBoundingClientRect();
  if (!table || !dealer || !table.width || !dealer.width) {
    if (model.session?.dealing) finishServerDeal(model.sessionId,game.handNumber);
    return;
  }
  const origin = {
    x:(dealer.left+dealer.width/2)*.65+(table.left+table.width/2)*.35,
    y:(dealer.top+dealer.height/2)*.65+(table.top+table.height/2)*.35
  };
  const deal = {targets:new Map(),landed:new Set(),animations:[],visuals:[],
    sessionId:model.session?.dealing ? model.sessionId : null,handNumber:game.handNumber};
  model.dealAnimation = deal;
  game.players.forEach(player => registerDealHand($(`#seat-${player.seat}`),player.seat));
  const deck = document.createElement('div');
  deck.className = 'deal-deck'; deck.setAttribute('aria-hidden','true');
  deck.innerHTML = '<span class="card-back"></span>'.repeat(4);
  deck.style.left = `${origin.x-25}px`; deck.style.top = `${origin.y-37}px`;
  document.body.append(deck); deal.visuals.push(deck);
  const flights = [];
  for (const packet of dealPackets(game.dealer)) for(let offset=0;offset<packet.count;offset++) {
    const key = `${packet.seat}:${packet.firstCard+offset}`;
    const target = deal.targets.get(key);
    if (!target) continue;
    const bounds = target.getBoundingClientRect();
    const width = target.offsetWidth || bounds.width;
    const height = target.offsetHeight || bounds.height;
    const left = bounds.left + (bounds.width - width) / 2;
    const top = bounds.top + (bounds.height - height) / 2;
    const matrix = window.getComputedStyle(target).transform.match(/^matrix\(([^)]+)\)$/);
    const rotation = matrix ? (() => {
      const values = matrix[1].split(',').map(Number);
      return Math.atan2(values[1], values[0]) * 180 / Math.PI;
    })() : 0;
    const visual = document.createElement('span');
    visual.className = 'card-back deal-card'; visual.setAttribute('aria-hidden','true');
    visual.style.width = `${width}px`; visual.style.height = `${height}px`;
    visual.style.left = `${left}px`; visual.style.top = `${top}px`;
    const x = origin.x-left-width/2;
    const y = origin.y-top-height/2;
    document.body.append(visual); deal.visuals.push(visual);
    const animation = visual.animate([
      {translate:`${x}px ${y}px`,opacity:0,rotate:'0deg'},
      {translate:`${x*.94}px ${y*.94-8}px`,opacity:1,offset:.08},
      {translate:'0 0',opacity:1,rotate:`${rotation}deg`}
    ],{duration:340,delay:packet.delay+offset*18,easing:'cubic-bezier(.2,.75,.25,1)',fill:'both'});
    deal.animations.push(animation);
    flights.push(animation.finished.then(() => {
      if (model.dealAnimation !== deal) return;
      deal.landed.add(key);
      const current = deal.targets.get(key);
      if (current) current.style.opacity = '';
      visual.remove();
    },() => {}));
  }
  syncInputState();
  Promise.all(flights).then(() => { if (model.dealAnimation === deal) cancelDeal(); });
}

async function finishServerDeal(sessionId,handNumber) {
  const key = `${sessionId}:${handNumber}`;
  if (model.dealCompleting === key) return;
  model.dealCompletedHand = key;
  model.dealCompleting = key;
  try {
    await api(`/api/sessions/${sessionId}/deal-complete?handNumber=${handNumber}`, {method:'POST',body:'{}'});
    if (model.sessionId === sessionId && model.session?.game?.handNumber === handNumber) {
      model.session.dealing = false;
      syncInputState();
      await refreshGame({notify:false});
    }
  } catch { /* Keep bidding locked; the next state refresh retries completion. */ }
  finally { if (model.dealCompleting === key) model.dealCompleting = null; }
}
