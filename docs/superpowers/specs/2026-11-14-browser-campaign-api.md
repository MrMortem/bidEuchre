# Campaign HTTP wire contract (v1)

All routes are `/api/campaign` plus the suffix below. GET `/api/campaign` bootstraps the persistent HttpOnly cookie. Clients never read or send a profile ID. Every response is `Cache-Control: no-store`. JSON properties are camelCase; enum values are exact C# names (case-sensitive strings, never integers). Physical card IDs are integers 0..23. Faces are `{code,suit,rank}` and are not identities.

Every mutation sends a JSON object with `expectedVersion` from the latest state. Live `/commands`, `/next-hand`, `/arrange-hand` also require integer `expectedRevision` and `expectedDealSerial` from `state.table`. Unknown or duplicate properties are rejected. No mutation is automatically retried. Refresh after transport failure. A 409 stale error requires a fresh choice.

Routes (POST unless marked):
- GET root: own state; dormant InMatch has `requiresContinue:true`, no table and bots remain stopped.
- `/new`, `/continue`, `/start-match`, `/forfeit`, `/next-hand`, `/shop/reroll`, `/result/continue`, `/victory/continue`, `/shop/leave`: version only, except next-hand also live revision/deal.
- `/commands`: live preconditions plus `command` (union below).
- `/arrange-hand`: live preconditions plus `orderedCardIds:int[]`.
- `/shop/buy`, `/shop/pull`: `offerId:string`.
- `/shop/buy-use`: `offerId`, `targetIds:int[]`.
- `/shop/sell`: `jokerId:string` enum.
- `/items/use`: `category:"Tarot"|"Sigil"`, `slot:int`, `targetIds:int[]`.
- `/items/discard`: `category`, `slot`.
- DELETE root: version only; explicitly deletes own save and returns empty state.
- GET `/catalog`: `{features:FeatureDto[]}`. Also embedded as `catalog` in state for simple clients.

Every successful route returns state directly (not wrapped):
```
{
 hasRun:boolean, version:string, phase:"Empty"|"Recovery"|"Intro"|"InMatch"|"Result"|"Shop"|"Victory"|"RunOver",
 requiresContinue:boolean, saveStatus:"saved"|"failed"|"corrupt", notice:string|null,
 run: {lives,ante,match,matchInAnte,boss,money,threshold,negativeThreshold,handLimit,opponentBonus,matchesWon,isOver,victoryShown}|null,
 table:TableDto|null, deck:CardDto[], inventory:InventoryDto|null,
 shop:ShopDto|null, pendingResult:{outcome:MatchOutcome,scores:int[],summary:{playerNames:string[],handHistory:HandResult[],publicEvents:string[],completionReason:string|null}|null}|null,
 capabilities:{[action]:{enabled:boolean,reason:string|null}}, catalog:FeatureDto[],
 feedback:{message:string,grants:{id:string,duplicate:boolean,copies:number,bonus:number}[]}|null
}
```
Forfeits have empty scores and optional null summary. Completed results retain public structured history and names in summary after runtime disposal/restart.

`version` is opaque; compare/send exactly. `Empty`/`Recovery` also have a token, including after deletion. New replaces the current run (UI confirms); Recovery means an existing unreadable/invalid file was preserved until explicit New/Delete.

`CardDto` = `{id:int,face:{code,suit,rank},enhancement:string|null,sigil:string|null}`. `deck` is the shared known deck, not live hands or overlays.

`InventoryDto` = `{jokers:[{id,value,sellValue,sell:{enabled,reason}}],tarot:[{slot,id,targetCount}],sigils:[{slot,id,targetCount}],gumballs:[{id,copies,bonus}],itemCount,itemSlots,jokerSlots}`. Gumball bonus is zero for PanicButton. Slot indices are per category, as in Core, but capacity is four SHARED Tarot + Sigil items, not four of each. Live targets are the human's current visible hand; between matches use the shared deck.

`FeatureDto` = `{id,category,key,name,description,price,targetCount}`. `id` is catalog slug/art key, e.g. `tarot:devilish-deal`; `key` is enum name used in inventory/shop/commands. `targetCount` is 1 for Sigils, shared App Tarot count for Tarots, 0 otherwise.

`ShopDto` = `{offers:[{id,category,featureId,price,sold,targetCount,buy:{enabled,reason},buyUse:{enabled,reason}}],rerollPrice,rerollCount,reroll:{enabled,reason}}`. Gumball offer's featureId is empty until bought. Pull feedback uses actual copy-count delta, so duplicates are reported.

`TableDto` = `{phase,handNumber,dealer,currentSeat,scores,players,auction,highBid,bidder,contract,tricksByTeam,gameWinner,handHistory,publicEvents,completionReason,revision,dealSerial,legalCommands,trick,lastTrick,events,pendingChoice,error}`.
- players: `[{seat,name,team,cardCount,visibleCards:CardDto[],isSittingOut}]`, seat0 viewer only; opponents only contain cards Core reveals.
- trick/lastTrick: `[{seat,card:CardDto,effectiveSuit}]`, historical card images retained.
- legalCommands: explicit command union below, never serialized abstract Core commands.
- auction, contract, handHistory (including scoreBreakdowns), publicEvents and scores use public Core view shapes. Contract `{bid,mode,trump,requiredTricks,isPartnersBest,isAlone}`. Hand history has structured team arithmetic including gumball bonuses.
- pendingChoice is display context only. Render legalCommands for actor-specific choices. currentSeat is the public campaign actor (including pending choices).

Command union (`kind` exact):
- `bid`: `level` enum Two/Three/Four/Five/Six/PartnersBest/Alone; optional `variant` enum Lowballer/Chaos/Poker/Duelist/Telepath/PartnersBest/Alone/PanicButton/HighLow
- `pass`, `panic-button`: no payload
- `play`: `cardId`; optional `effectiveSuit` Clubs/Diamonds/Hearts/Spades
- `exchange`, `swap-partner-card`, `reveal-partner-card`: `cardId`
- `choose-contract`: `mode` High/Low/Trump; optional `trump` suit (required only for Trump)
- `use-tarot`: `slot`, `targetIds`
- `use-sigil`: `slot`, `targetId`
- `choose-reveal-seat`, `select-opponent`: `targetSeat`

Capabilities keys: `new`, `continue`, `delete`, `start-match`, `forfeit`, `next-hand`, `commands`, `arrange-hand`, `items-use`, `items-discard`, `result-continue`, `victory-continue`, `shop-leave`, `shop-reroll`, `shop-sell`. Per-offer and reroll eligibility also appear on shop. Capabilities are UI hints, not authorization. Core/App validates all mutations. Live item use is available through commands when the campaign allows it, even if not enumerated in legalCommands; report rejected use visibly.

Errors: `{code,error,refresh:boolean}`. 400 `invalid_request` / `illegal_action`; 409 `profile_required`, `stale_state`, `wrong_phase`; 404 `no_campaign`; 429 `capacity`; 409 `save_unavailable` on deletion failure. Policy middleware retains existing 403/415 `{error}`. Stale does not mutate. Accepted mutation with failed persistence returns 200 and state `saveStatus:"failed"` with warning: refresh, never replay. No RNG, token, path, full snapshot, hidden hands or other wallets are exposed.
