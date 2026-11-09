/* Public-state event tracking. Never stores or compares a player's private hand. */
var TableMotion = class {
  constructor() { this.reset(); }
  reset() { this.context = null; this.previous = null; }
  update(game, context) {
    const cue = { deal:false, highBidChanged:false, addedPlays:new Set(), turnSeat:null, winnerSeat:null, scoreTeams:new Set() };
    const last = game?.completedTricks.at(-1);
    const frame = game ? {
      bid:game.highBid != null && game.bidder != null ? `${game.bidder}:${game.highBid}` : null,
      hand:game.handNumber, turn:game.currentSeat, completed:game.completedTricks.length,
      plays:new Set((game.currentTrick.length ? game.currentTrick : last?.plays ?? []).map(play => `${play.seat}:${play.card.code}`)),
      winner:last?.winner ?? null, scores:[...game.scores]
    } : null;
    if (this.context === context && frame) {
      const old = this.previous;
      cue.deal = !old || frame.hand !== old.hand;
      if (old) {
        if (frame.hand === old.hand) {
          cue.highBidChanged = frame.bid !== null && frame.bid !== old.bid && ['Bidding','ChoosingContract'].includes(game.phase);
          for (const key of frame.plays) if (!old.plays.has(key)) cue.addedPlays.add(key);
          if (frame.completed > old.completed) cue.winnerSeat = frame.winner;
        }
        if (frame.turn !== old.turn) cue.turnSeat = frame.turn;
        frame.scores.forEach((score, team) => { if (score !== old.scores[team]) cue.scoreTeams.add(team); });
      }
    }
    this.context = context;
    this.previous = frame;
    return cue;
  }
};
