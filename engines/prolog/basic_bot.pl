#!/usr/bin/env swipl

:- module(basic_bideuchre_bot, [main/0, choose_action/2, decode_position/2]).

:- use_module(library(base64)).
:- use_module(library(http/json)).
:- use_module(library(readutil)).

:- dynamic current_position/1.

/** <module> A deliberately basic BEUCI engine

This engine demonstrates the smallest useful external-bot integration. It does
not try to evaluate a hand. Instead, it passes when allowed and otherwise picks
the first value supplied by the host's authoritative legalActions object.
*/

main :-
    set_stream(user_input, encoding(utf8)),
    set_stream(user_output, encoding(utf8)),
    command_loop.

command_loop :-
    read_line_to_string(user_input, Line),
    (   Line == end_of_file
    ->  true
    ;   normalize_space(string(CommandLine), Line),
        (   CommandLine == ""
        ->  Continue = true
        ;   catch(
                dispatch(CommandLine, Continue),
                Error,
                (write_protocol_error(Error), Continue = true))
        ),
        (   Continue == true
        ->  command_loop
        ;   true
        )
    ).

dispatch(CommandLine, Continue) :-
    split_string(CommandLine, " \t", " \t", Parts),
    Parts = [CommandText|Arguments],
    string_lower(CommandText, Command),
    dispatch_command(Command, Arguments, Continue).

dispatch_command("beuci", _, true) :-
    write_line('id name "Basic Prolog Bot"'),
    write_line('id author "Bid Euchre Project"'),
    write_line('protocol bideuchre 1'),
    write_line(beuciok).
dispatch_command("isready", _, true) :-
    write_line(readyok).
dispatch_command("newgame", _, true) :-
    retractall(current_position(_)).
dispatch_command("setoption", _, true).
dispatch_command("position", [Payload], true) :-
    store_position(Payload).
dispatch_command("position", _, _) :-
    throw(error(protocol_error('position requires one base64url payload'), _)).
dispatch_command("go", _, true) :-
    (   current_position(valid(Position))
    ->  choose_action(Position, Action),
        write_line(Action)
    ;   current_position(invalid(Message))
    ->  format(string(Error), 'invalid position payload: ~s', [Message]),
        write_error(Error)
    ;   throw(error(protocol_error('a position must be supplied before go'), _))
    ).
dispatch_command("stop", _, true).
dispatch_command("quit", _, false).
dispatch_command(Command, _, true) :-
    format(string(Message), 'unknown-command ~s', [Command]),
    write_error(Message).

store_position(Payload) :-
    retractall(current_position(_)),
    (   catch(decode_position(Payload, Position), Error, true)
    ->  (   var(Error), nonvar(Position)
        ->  assertz(current_position(valid(Position)))
        ;   message_text(Error, Message),
            assertz(current_position(invalid(Message)))
        )
    ;   assertz(current_position(invalid('position payload could not be decoded')))
    ).

decode_position(PayloadString, Position) :-
    atom_string(PayloadAtom, PayloadString),
    catch(base64_encoded(JsonAtom, PayloadAtom,
                         [charset(url), padding(false), encoding(utf8), as(atom)]), Error,
          throw(error(protocol_error(Error), _))),
    catch(atom_json_dict(JsonAtom, Position, []), Error,
          throw(error(protocol_error(Error), _))).

choose_action(Position, Action) :-
    get_dict(game, Position, Game),
    get_dict(phase, Game, Phase),
    get_dict(legalActions, Game, Legal),
    action_for_phase(Phase, Legal, Action),
    !.
choose_action(_, _) :-
    throw(error(protocol_error('position has no legal action for this phase'), _)).

action_for_phase("Bidding", Legal, "bestaction pass") :-
    get_dict(canPass, Legal, true),
    !.
action_for_phase("Bidding", Legal, Action) :-
    get_dict(bids, Legal, [Bid|_]),
    bid_token(Bid, Token),
    format(string(Action), 'bestaction bid ~s', [Token]).
action_for_phase("ChoosingContract", Legal, Action) :-
    get_dict(contractModes, Legal, [Mode|_]),
    contract_action(Mode, Legal, Action).
action_for_phase("ExchangingBidderCard", Legal, Action) :-
    card_action(exchange, Legal, Action).
action_for_phase("ExchangingPartnerCard", Legal, Action) :-
    card_action(exchange, Legal, Action).
action_for_phase("Playing", Legal, Action) :-
    card_action(play, Legal, Action).

bid_token("Three", "3").
bid_token("Four", "4").
bid_token("Five", "5").
bid_token("Six", "6").
bid_token("PartnersBest", "partnersbest").
bid_token("Alone", "alone").

contract_action("High", _, "bestaction contract high").
contract_action("Low", _, "bestaction contract low").
contract_action("Trump", Legal, Action) :-
    get_dict(trumpSuits, Legal, [Suit|_]),
    suit_token(Suit, Token),
    format(string(Action), 'bestaction contract trump ~s', [Token]).

suit_token("Clubs", "clubs").
suit_token("Diamonds", "diamonds").
suit_token("Hearts", "hearts").
suit_token("Spades", "spades").

card_action(Verb, Legal, Action) :-
    get_dict(cards, Legal, [Card|_]),
    get_dict(code, Card, Code),
    string_upper(Code, UpperCode),
    valid_card_code(UpperCode),
    string_lower(UpperCode, LowerCode),
    format(string(Action), 'bestaction ~w ~s', [Verb, LowerCode]).

valid_card_code(Code) :-
    string_codes(Code, [Rank, Suit]),
    memberchk(Rank, [0'9, 0'T, 0'J, 0'Q, 0'K, 0'A]),
    memberchk(Suit, [0'C, 0'D, 0'H, 0'S]).

write_line(Line) :-
    (   string(Line)
    ->  format(user_output, '~s~n', [Line])
    ;   format(user_output, '~w~n', [Line])
    ),
    flush_output(user_output).

write_protocol_error(error(protocol_error(Message), _)) :-
    !,
    message_text(Message, Text),
    write_error(Text).
write_protocol_error(Error) :-
    message_to_string(Error, Text),
    write_error(Text).

message_text(Message, Text) :-
    (   string(Message)
    ->  Text = Message
    ;   atom(Message)
    ->  atom_string(Message, Text)
    ;   message_to_string(Message, Text)
    ).

write_error(Message) :-
    split_string(Message, "\r\n", "", Parts),
    atomics_to_string(Parts, " ", SingleLine),
    format(user_output, 'error ~s~n', [SingleLine]),
    flush_output(user_output).

:- initialization(main, main).
