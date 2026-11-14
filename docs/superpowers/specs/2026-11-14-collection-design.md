# Main-menu collection

Add a Collection button to the native main menu and a read-only browser of every
defined item, available without a campaign save or discovery requirements.

The screen uses the existing table backdrop and menu palette. A fixed heading and
Back button sit above a category sidebar and a vertically scrolling, wrapping
gallery. Categories are Jokers, Tarot cards, Gumballs, Playing cards, Card
enhancements, and Sigils. Each displays its count, artwork, name, and description.
Changing categories resets the gallery to its top. The sidebar can scroll at
small window sizes; keyboard users can tab into the gallery and scroll it.

## Approaches

A single long list would be simple but make finding individual item families
slow. A separate detail screen per item would add unnecessary navigation for
short descriptions. The implementation choice is a category gallery with each
description visible alongside its artwork; no extra inspection click is needed.

## Data and constraints

Use FeatureCatalog.All for the five campaign item families and
GameRules.CreateDeck() for the 24 playing cards. Reuse PixelArt and
GumballMachine.Ball so collection artwork matches gameplay, including existing
fallback gumballs. Enhancement previews use an enhanced sample playing card.
No ownership tracking, shop actions, new item types, or save changes are needed.
Keep the optional web client unchanged; its lobby has no campaign main menu.
