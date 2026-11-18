# Consumable targeting

The user requested that Sigils and Tarots act only on cards in hand during gameplay, with six random cards shown at designated shop opportunities. They explicitly selected **new shop purchases only**; held inventory cannot be used in the shop or before a match.

Keep existing human-turn and current-hand validation in the match engine. A played card, opponent card, or other shared-deck card is never a legal gameplay target. Gambler requires exactly two selected cards from the current hand. Effects that target no cards, such as Taxes and Astronomer, retain their existing behavior.

A new Sigil offer has a saved six-card sample. The Fortune Teller draws a saved six-card sample when tickets are paid for and printed; every ticket in that print uses the same sample. Immediate application is restricted to this sample, including both selected Gambler targets. Cards are distinct physical IDs so duplicate faces remain independent. A purchased item may still be kept for later gameplay subject to existing inventory capacity.

Save the sample with its offer. Reopening, cancellation and reload retain it; new stock from an authorized paid reroll may create a new opportunity. Legacy shops receive any missing sample once without changing stock or already printed tickets. Invalid/stale/off-sample requests must leave payment, inventory, deck, RNG and revision unchanged.

Desktop and web display the same six eligible cards. Remove held-item Use controls outside matches while retaining discard and read-only deck inspection. The web client must expose the existing Fortune Teller print/keep/use operations with the same server version checks.

Approach: persist samples on ShopOffer and validate them in the application domain methods. Client-only sampling would allow forged targets and rerolls on reopening; a shared sample for the whole visit would not represent the separate purchase opportunities requested. The per-offer approach preserves the existing shop model with one optional field.

Follow-up: the user requested two selectable Gambler targets. Random selection has been replaced with exactly two selected IDs; see `docs/superpowers/plans/2026-11-15-gambler-targets.md`.
