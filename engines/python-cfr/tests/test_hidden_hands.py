import random
import unittest

from test_simulator import card, position
from bideuchre_cfr.actions import CARD_CODES
from bideuchre_cfr.simulator import _effective_suit, _state_from_position


class HiddenHandTests(unittest.TestCase):
    def view(self, mode='High', trump=None, completed=False, lead='AC', discard='9D'):
        plays = [{'seat': 0, 'card': card(lead)}, {'seat': 1, 'card': card(discard)}]
        hand = ['9H', 'TH', 'JH', 'QH', 'KH', 'AH']
        if completed:
            plays += [{'seat': 2, 'card': card('AH')}, {'seat': 3, 'card': card('9S')}]
            hand.remove('AH')
        return position('Playing', hand, seat=2,
            counts=(5, 5, 5, 5) if completed else (5, 5, 6, 6),
            contract={'bid': 'Four', 'mode': mode, 'trump': trump}, bidder=0,
            completed_tricks=[{'plays': plays}] if completed else [],
            current_trick=[] if completed else plays)

    def test_high_low_completed_and_current_voids_preserve_all_cards(self):
        for mode in ('High', 'Low'):
            for completed in (False, True):
                view = self.view(mode, completed=completed)
                for seed in range(20):
                    with self.subTest(mode=mode, completed=completed, seed=seed):
                        state = _state_from_position(view, random.Random(seed))
                        self.assertFalse(any(code.endswith('C') for code in state.hands[1]))
                        self.assertEqual([p['cardCount'] for p in view['game']['players']], list(map(len, state.hands)))
                        self.assertEqual([c['code'] for c in view['game']['players'][2]['cards']], state.hands[2])
                        played = [p['card']['code'] for t in view['game']['completedTricks'] for p in t['plays']]
                        played += [p['card']['code'] for p in view['game']['currentTrick']]
                        self.assertEqual(sorted(CARD_CODES), sorted(played + sum(state.hands, [])))

    def test_left_bower_lead_proves_trump_void(self):
        for completed in (False, True):
            view = self.view('Trump', 'Spades', lead='JC', completed=completed)
            for seed in range(20):
                state = _state_from_position(view, random.Random(seed))
                self.assertFalse(any(_effective_suit(c, state.contract) == 'S' for c in state.hands[1]))

    def test_discarding_left_bower_proves_printed_led_suit_void(self):
        for completed in (False, True):
            view = self.view('Trump', 'Spades', discard='JC', completed=completed)
            for seed in range(20):
                state = _state_from_position(view, random.Random(seed))
                self.assertFalse(any(_effective_suit(c, state.contract) == 'C' for c in state.hands[1]))

    def test_void_constraints_allow_left_bower_in_its_effective_suit(self):
        view = self.view('Trump', 'Spades', discard='9D')
        # Public evidence says seat 1 is void Clubs, but JC is a Spade.
        view['game']['players'][1]['cards'] = [card(c) for c in ['JC', 'TD', 'JD', 'QD', 'KD']]
        state = _state_from_position(view, random.Random(0))
        self.assertIn('JC', state.hands[1])

    def test_left_bower_following_trump_does_not_prove_a_void(self):
        view = self.view('Trump', 'Spades', lead='AS', discard='JC')
        view['game']['players'][1]['cards'] = [card(c) for c in ['9S', 'TS', 'QS', 'KS', 'TD']]
        state = _state_from_position(view, random.Random(0))
        self.assertIn('9S', state.hands[1])

    def test_conflicting_visible_hand_fails_explicitly(self):
        view = self.view()
        view['game']['players'][1]['cards'] = [card(c) for c in ['JC', 'TD', 'JD', 'QD', 'KD']]
        with self.assertRaisesRegex(ValueError, 'consistent|void'):
            _state_from_position(view, random.Random(0))

    def test_impossible_hidden_capacity_fails_explicitly(self):
        view = self.view()
        view['game']['players'][0]['cardCount'] = 0
        view['game']['players'][1]['cardCount'] = 16
        view['game']['players'][3]['cardCount'] = 0
        with self.assertRaisesRegex(ValueError, 'consistent|void'):
            _state_from_position(view, random.Random(0))

    def test_private_exchange_card_is_pinned_without_breaking_voids(self):
        view = self.view()
        view['game']['contract']['isPartnersBest'] = True
        view['game']['bidder'] = 3  # remembered card belongs to partner seat 1
        view['cfrPrivateMemory'] = {'partnersBestReceived': 'TD'}
        for seed in range(20):
            state = _state_from_position(view, random.Random(seed))
            self.assertIn('TD', state.hands[1])
            self.assertFalse(any(c.endswith('C') for c in state.hands[1]))
            self.assertEqual([5, 5, 6, 6], list(map(len, state.hands)))
        view['cfrPrivateMemory']['partnersBestReceived'] = 'TC'
        with self.assertRaisesRegex(ValueError, 'consistent|void'):
            _state_from_position(view, random.Random(0))
