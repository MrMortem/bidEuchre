const model = {
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
  rulesLoaded: false
};

const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];

document.addEventListener('DOMContentLoaded', async () => {
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
  $('#viewer-seat').addEventListener('change', async event => {
    model.viewerSeat = event.target.value === '' ? null : Number(event.target.value);
    await refreshGame();
  });
}

function bindForms() {
  $('#create-session-form').addEventListener('submit', createSession);
  $('#load-engine-form').addEventListener('submit', loadExternalEngine);
}

function showView(name) {
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
  const response = await fetch(path, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) }
  });
  const contentType = response.headers.get('content-type') || '';
  const body = contentType.includes('json') ? await response.json() : await response.text();
  if (!response.ok) throw new Error(body?.error || body || `Request failed (${response.status})`);
  return body;
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

async function loadExternalEngine(event) {
  event.preventDefault();
  const button = event.submitter;
  button.disabled = true;
  button.textContent = 'Handshaking…';
  try {
    const engine = await api('/api/engines/load', {
      method: 'POST',
      body: JSON.stringify({ executable: $('#engine-executable').value, arguments: $('#engine-arguments').value })
    });
    toast(`${engine.name} loaded successfully.`);
    event.target.reset();
    await loadEngines();
  } catch (error) { toast(error.message, true); }
  finally { button.disabled = false; button.textContent = 'Handshake & load'; }
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
  const list = $('#session-list');
  if (!model.sessions.length) {
    list.innerHTML = '<div class="empty-state"><div><strong>No tables yet</strong>Create a session and choose who takes each seat.</div></div>';
    return;
  }
  list.innerHTML = model.sessions.map(session => `
    <article class="session-card" data-id="${session.id}">
      <div class="session-card-top"><h3>${escapeHtml(session.name)}</h3><span class="phase-chip">${splitWords(session.phase)}</span></div>
      <div class="mini-score"><strong>${session.scores[0]}</strong><span>—</span><strong>${session.scores[1]}</strong></div>
      <div class="seat-dots">${session.seats.map(seat => `<span>${seat.kind === 'Bot' ? '⚙' : '●'} ${escapeHtml(seat.name)}</span>`).join('')}</div>
    </article>`).join('');
  $$('.session-card').forEach(card => card.addEventListener('click', () => openSession(card.dataset.id)));
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
  stopPolling();
  model.sessionId = id;
  model.session = null;
  model.renderMode = null;
  const summary = model.sessions.find(session => session.id === id);
  model.viewerSeat = summary?.seats.find(seat => seat.kind === 'Human')?.seat ?? null;
  showView('game');
  await refreshGame();
  startPolling();
}

async function startCurrentSession() {
  if (model.startPending) return;
  const button = $('#start-game');
  model.startPending = true;
  button.disabled = true;
  button.setAttribute('aria-busy', 'true');
  button.textContent = 'Starting bots…';
  try {
    await api(`/api/sessions/${model.sessionId}/start`, { method: 'POST', body: '{}' });
    await refreshGame();
  } catch (error) { toast(error.message, true); }
  finally {
    model.startPending = false;
    button.disabled = false;
    button.removeAttribute('aria-busy');
    button.textContent = 'Start game';
  }
}

function refreshGame({ notify = true } = {}) {
  const operation = async () => {
    const sessionId = model.sessionId;
    const viewerSeat = model.viewerSeat;
    if (!sessionId) return;

    try {
      const query = viewerSeat === null ? '' : `?seat=${viewerSeat}`;
      const session = await api(`/api/sessions/${sessionId}${query}`);

      // A slow response for a table or hand we have already left must not repaint the UI.
      if (sessionId !== model.sessionId || viewerSeat !== model.viewerSeat) return;
      model.session = session;
      renderGame();
    } catch (error) {
      if (notify && sessionId === model.sessionId && viewerSeat === model.viewerSeat) {
        toast(error.message, true);
      }
    }
  };

  // Serialize manual refreshes and polling so responses can never arrive out of order.
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

  if (!session.started || !session.game) {
    resetBoard();
    return;
  }

  const game = session.game;
  model.renderMode = 'game';
  $('#team-zero-score').textContent = game.scores[0];
  $('#team-one-score').textContent = game.scores[1];
  $('#phase-label').textContent = `${splitWords(game.phase)} · Hand ${game.handNumber}`;
  $('#contract-label').textContent = contractName(game);
  $('#dealer-label').textContent = `Seat ${game.dealer + 1} deals · ${game.tricksByTeam[0]}–${game.tricksByTeam[1]} tricks`;
  $('#trick-count').textContent = `${game.completedTricks.length} / 6 tricks`;
  $('#game-error').textContent = session.error || '';
  $('#game-error').classList.toggle('hidden', !session.error);

  game.players.forEach(player => renderPlayer(player, game));
  renderTrick(game);
  renderActions(game);
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
  $('#dealer-label').textContent = 'Dealer not chosen';
  model.session.seats.forEach(seat => {
    const position = $(`#seat-${seat.seat}`);
    position.removeAttribute('data-render-key');
    position.innerHTML = `<div class="player-tag"><span>${escapeHtml(seat.name)}</span><span>${seat.kind === 'Bot' ? '⚙ Bot' : '● Human'}</span></div><div class="card-row">${backs(6)}</div>`;
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
      <span class="player-name">${seatConfig?.kind === 'Bot' ? '⚙' : '●'} ${escapeHtml(player.name)}</span>
      ${game.dealer === player.seat ? '<span class="dealer">D</span>' : ''}
      ${player.isSittingOut ? '<span>Sitting out</span>' : ''}
      <span class="tricks">${game.tricksByTeam[player.team]} tricks</span>
    </div>
    <div class="card-row">${cards}</div>`;
  position.dataset.renderKey = renderKey;
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
    plays.map(play => [play.seat, cardCode(play.card)]),
    completed?.number ?? null,
    completed?.winner ?? null
  ]);
  if (area.dataset.renderKey === renderKey) return;
  area.innerHTML = plays.map(play => `<div class="trick-card seat-${play.seat}">${cardHtml(play.card, false)}</div>`).join('') +
    (completed ? `<span class="trick-result">${escapeHtml(game.players[completed.winner].name)} won trick ${completed.number}</span>` : '');
  area.dataset.renderKey = renderKey;
}

function renderActions(game) {
  const title = $('#action-title');
  const help = $('#action-help');
  const actions = $('#action-buttons');
  const buttons = [];
  let titleText;
  let helpText;

  if (game.phase === 'HandComplete') {
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

async function sendAction(action) {
  if (model.actionPending) return;
  setActionBusy(true);
  try {
    await api(`/api/sessions/${model.sessionId}/actions`, {
      method: 'POST', body: JSON.stringify({ seat: model.viewerSeat, ...action })
    });
    await refreshGame();
  } catch (error) { toast(error.message, true); }
  finally { setActionBusy(false); }
}

function playOrExchange(card) {
  const phase = model.session?.game?.phase;
  if (phase !== 'Playing' && !phase?.startsWith('Exchanging')) return;
  sendAction({ type: phase === 'Playing' ? 'play' : 'exchange', card });
}

async function nextHand() {
  if (model.actionPending) return;
  setActionBusy(true);
  try {
    await api(`/api/sessions/${model.sessionId}/next-hand`, { method: 'POST', body: '{}' });
    await refreshGame();
  } catch (error) { toast(error.message, true); }
  finally { setActionBusy(false); }
}

async function selectViewerSeat(seat) {
  model.viewerSeat = seat;
  $('#viewer-seat').value = String(seat);
  await refreshGame();
}

function setActionBusy(busy) {
  model.actionPending = busy;
  const actions = $('#action-buttons');
  actions.setAttribute('aria-busy', String(busy));
  actions.querySelectorAll('button').forEach(button => button.disabled = busy);
  $$('.playing-card.legal').forEach(button => button.disabled = busy);
}

async function loadRules() {
  try {
    const response = await fetch('/api/rules');
    const markdown = await response.text();
    $('#rules-content').innerHTML = renderMarkdown(markdown);
    model.rulesLoaded = true;
  } catch (error) { $('#rules-content').textContent = error.message; }
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
  const suit = suitSymbol(card.suit);
  const red = card.suit === 'Hearts' || card.suit === 'Diamonds';
  const rank = cardRank(card);
  const code = cardCode(card);
  const label = `${rank} of ${card.suit}`;
  return `<button type="button" class="playing-card ${red ? 'red' : ''} ${legal ? 'legal' : ''}" aria-label="${escapeHtml(label)}" data-card="${escapeHtml(code)}" ${legal ? '' : 'disabled'}><strong>${rank}</strong><span class="suit" aria-hidden="true">${suit}</span></button>`;
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
    if (token !== model.pollToken) return;
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
