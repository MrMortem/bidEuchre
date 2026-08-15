:- begin_tests(basic_bideuchre_bot).

:- use_module(basic_bot).
:- use_module(library(base64)).
:- use_module(library(http/json)).

legal(CanPass, Bids, Modes, Suits, Cards,
      _{canPass:CanPass, bids:Bids, contractModes:Modes,
        trumpSuits:Suits, cards:Cards}).

position(Phase, Legal, _{seat:1, game:_{phase:Phase, legalActions:Legal}}).

test(decodes_unpadded_base64url_json) :-
    Source = _{seat:1, game:_{phase:"Bidding",
                              legalActions:_{canPass:true, bids:[],
                                             contractModes:[], trumpSuits:[], cards:[]}}},
    atom_json_dict(Json, Source, []),
    base64_encoded(Json, Payload,
                   [charset(url), padding(false), encoding(utf8), as(atom)]),
    atom_string(Payload, PayloadString),
    decode_position(PayloadString, Decoded),
    assertion(Decoded.seat == 1),
    assertion(Decoded.game.phase == "Bidding").

test(passes_when_legal) :-
    legal(true, ["Three"], [], [], [], Legal),
    position("Bidding", Legal, Position),
    choose_action(Position, Action),
    assertion(Action == "bestaction pass").

test(all_bid_tokens) :-
    forall(
        member(Bid-Token,
               ["Three"-"3", "Four"-"4", "Five"-"5", "Six"-"6",
                "PartnersBest"-"partnersbest", "Alone"-"alone"]),
        ( legal(false, [Bid], [], [], [], Legal),
          position("Bidding", Legal, Position),
          choose_action(Position, Action),
          format(string(Expected), 'bestaction bid ~s', [Token]),
          assertion(Action == Expected)
        )).

test(chooses_high_contract) :-
    legal(false, [], ["High", "Low", "Trump"],
          ["Clubs", "Diamonds", "Hearts", "Spades"], [], Legal),
    position("ChoosingContract", Legal, Position),
    choose_action(Position, Action),
    assertion(Action == "bestaction contract high").

test(chooses_required_trump_contract) :-
    legal(false, [], ["Trump"], ["Hearts", "Spades"], [], Legal),
    position("ChoosingContract", Legal, Position),
    choose_action(Position, Action),
    assertion(Action == "bestaction contract trump hearts").

test(chooses_low_contract) :-
    legal(false, [], ["Low", "Trump"], ["Clubs"], [], Legal),
    position("ChoosingContract", Legal, Position),
    choose_action(Position, Action),
    assertion(Action == "bestaction contract low").

test(all_trump_suit_tokens) :-
    forall(
        member(Suit-Token,
               ["Clubs"-"clubs", "Diamonds"-"diamonds",
                "Hearts"-"hearts", "Spades"-"spades"]),
        ( legal(false, [], ["Trump"], [Suit], [], Legal),
          position("ChoosingContract", Legal, Position),
          choose_action(Position, Action),
          format(string(Expected), 'bestaction contract trump ~s', [Token]),
          assertion(Action == Expected)
        )).

test(handles_bidder_exchange) :-
    legal(false, [], [], [], [_{code:"9C"}, _{code:"AS"}], Legal),
    position("ExchangingBidderCard", Legal, Position),
    choose_action(Position, Action),
    assertion(Action == "bestaction exchange 9c").

test(handles_partner_exchange) :-
    legal(false, [], [], [], [_{code:"TD"}, _{code:"KH"}], Legal),
    position("ExchangingPartnerCard", Legal, Position),
    choose_action(Position, Action),
    assertion(Action == "bestaction exchange td").

test(plays_first_legal_card) :-
    legal(false, [], [], [], [_{code:"JD"}, _{code:"AC"}], Legal),
    position("Playing", Legal, Position),
    choose_action(Position, Action),
    assertion(Action == "bestaction play jd").

test(rejects_invalid_card_code,
     [throws(error(protocol_error('position has no legal action for this phase'), _))]) :-
    legal(false, [], [], [], [_{code:"NX"}], Legal),
    position("Playing", Legal, Position),
    choose_action(Position, _).

test(rejects_inactive_phase,
     [throws(error(protocol_error('position has no legal action for this phase'), _))]) :-
    legal(false, [], [], [], [], Legal),
    position("HandComplete", Legal, Position),
    choose_action(Position, _).

:- end_tests(basic_bideuchre_bot).

:- initialization(run_tests, main).
