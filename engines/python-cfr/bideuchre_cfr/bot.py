"""BEUCI process for the autosaving PyTorch CFR Bid Euchre engine."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import random
import shlex
import sys
from typing import Any
import uuid

from .actions import (
    ACTION_COUNT,
    CARD_CODES,
    EXCHANGE_ACTION_OFFSET,
    decode_position,
    format_action,
    legal_action_ids,
)
from .encoding import encode_position, information_state_key
from .model import legal_mask, regret_matching
from .simulator import CounterfactualEvaluator
from .storage import LearningStore
from .trainer import CFRTrainer

ENGINE_NAME = "PyTorch CFR Bot"
ENGINE_AUTHOR = "Bid Euchre Project"


class CFRBot:
    def __init__(
        self,
        state_dir: Path,
        *,
        rollout_samples: int = 4,
        seed: int = 1946,
        deterministic: bool = False,
        train_batch: int = 4,
    ) -> None:
        self.store = LearningStore(state_dir)
        self.evaluator = CounterfactualEvaluator(samples=rollout_samples, seed=seed)
        self.random = random.Random(seed ^ os.getpid())
        self.seed = seed
        self.deterministic = deterministic
        self.train_batch = train_batch
        self._position: dict[str, Any] | None = None
        self._scores_by_hand: dict[tuple[str, int, int], tuple[float, float]] = {}
        self._game_key = uuid.uuid4().hex
        self._exchange_memory: dict[int, str] = {}
        self.trainer = CFRTrainer(self.store, seed=seed)

    def new_game(self) -> None:
        self._position = None
        self._scores_by_hand.clear()
        self._game_key = uuid.uuid4().hex
        self._exchange_memory.clear()

    def observe(self, position: dict[str, Any]) -> None:
        previous_position = self._position
        game = position.get("game", {})
        phase = game.get("phase")
        if phase == "ExchangingPartnerCard" and previous_position is not None:
            self._capture_received_exchange(position, previous_position)
        self._position = position
        if phase not in ("HandComplete", "GameComplete"):
            return
        # Terminal observations are silent. Learn their actual result and save
        # without emitting protocol output; failures are retried on the next
        # terminal observation rather than poisoning the next go response.
        try:
            seat = int(position["seat"])
            hand = int(game["handNumber"])
            game_key = self._game_key
            scores = game.get("scores", [0, 0])
            current = (float(scores[0]), float(scores[1]))
            previous = self._scores_by_hand.get(
                (game_key, seat, hand), (0.0, 0.0)
            )
            team = seat % 2
            own_delta = current[team] - previous[team]
            opponent_delta = current[1 - team] - previous[1 - team]
            self.store.apply_terminal_reward(
                hand, seat, own_delta - opponent_delta, game_key=game_key
            )
            with self.store.training_lock():
                self.trainer = CFRTrainer(self.store, seed=self.seed)
                _, reward_ids = self.trainer.train_terminal_rewards()
                if reward_ids:
                    self.trainer.save()
                    self.store.mark_rewards_trained(reward_ids)
        except Exception as error:
            print(f"terminal training deferred: {error}", file=sys.stderr)

    def choose(self) -> str:
        if self._position is None:
            raise ValueError("a position must be supplied before go")
        position = self._position
        legal = legal_action_ids(position)
        if not legal:
            raise ValueError("position has no legal action")

        model_position = self._with_exchange_memory(position)
        features = encode_position(model_position)
        game = position["game"]
        seat = int(position["seat"])
        hand = int(game["handNumber"])
        game_key = self._game_key
        scores = game.get("scores", [0, 0])
        self._scores_by_hand.setdefault(
            (game_key, seat, hand), (float(scores[0]), float(scores[1]))
        )

        with self.store.training_lock():
            # Multiple seat processes share one checkpoint. Reloading a fresh
            # trainer under the lock incorporates the previous seat's update.
            self.trainer = CFRTrainer(self.store, seed=self.seed)
            information_key = information_state_key(model_position)
            cumulative_regrets, strategy_sum, visits = self.store.get_information_set(
                information_key, ACTION_COUNT
            )
            network_regrets, _, _ = self.trainer.inference(features)
            mask = legal_mask(legal)
            if visits:
                regret_values = self.trainer.torch.tensor(cumulative_regrets)
            else:
                regret_values = network_regrets
            regret_strategy = regret_matching(regret_values, mask)
            # Sampled CFR traverses with the current regret-matched strategy.
            # The accumulated average remains a separate training target.
            strategy = [float(value) for value in regret_strategy]
            action_id = self._select_action(legal, strategy)

            estimates = self.evaluator.evaluate(model_position, legal)
            counterfactual = [0.0] * ACTION_COUNT
            for legal_id, estimate in zip(legal, estimates, strict=True):
                counterfactual[legal_id] = float(estimate)

            baseline = sum(strategy[action] * counterfactual[action] for action in legal)
            for action in legal:
                cumulative_regrets[action] = max(
                    0.0, cumulative_regrets[action] + counterfactual[action] - baseline
                )
                strategy_sum[action] += strategy[action]
            self.store.update_information_set(
                information_key,
                cumulative_regrets,
                strategy_sum,
                visits + 1,
            )

            # This transaction completes before bestaction is printed, so every
            # played decision is durable even if the process dies immediately.
            decision_id = self.store.append_decision(
                information_key=information_key,
                features=features,
                legal_actions=legal,
                action_id=action_id,
                strategy=strategy,
                counterfactual=counterfactual,
                hand_number=hand,
                seat=seat,
                game_key=game_key,
            )
            self._remember_exchange_choice(position, action_id)
            _, trained_ids = self.trainer.train_pending(
                maximum=self.train_batch, through_id=decision_id
            )
            self.trainer.save()
            # A row is acknowledged only after its weights are durably replaced.
            # A crash earlier causes an idempotent at-least-once replay.
            self.store.mark_trained(trained_ids)

        return format_action(action_id)

    def save(self) -> None:
        # Every played action is saved inside choose() while holding the shared
        # lock. A stale process must not overwrite another seat's newer model
        # merely because the host asked it to quit later.
        return

    def _select_action(self, legal: list[int], strategy: list[float]) -> int:
        if self.deterministic:
            return max(legal, key=lambda action_id: (strategy[action_id], -action_id))
        threshold = self.random.random()
        cumulative = 0.0
        for action_id in legal:
            cumulative += strategy[action_id]
            if threshold <= cumulative:
                return action_id
        return legal[-1]

    def _capture_received_exchange(
        self, position: dict[str, Any], previous: dict[str, Any] | None
    ) -> None:
        game = position["game"]
        if game.get("phase") != "ExchangingPartnerCard":
            return
        seat = int(position["seat"])
        hand = int(game["handNumber"])
        bidder = game.get("bidder")
        if bidder is None or seat != (int(bidder) + 2) % 4:
            return
        visible = next(player for player in game["players"] if player["seat"] == seat)["cards"]
        if visible:
            # The seventh card is the one received from the bidder. The partner
            # process saw six cards earlier only if it bid; derive conservatively
            # from the card-count transition and retain the chosen returned card
            # later through the decision record.
            previous_codes: set[str] = set()
            if previous is not None:
                previous_player = next(
                    (player for player in previous["game"].get("players", []) if player.get("seat") == seat),
                    None,
                )
                if previous_player and previous_player.get("cards"):
                    previous_codes = {
                        str(card["code"]).upper() for card in previous_player["cards"]
                    }
            current_codes = {str(card["code"]).upper() for card in visible}
            received = current_codes - previous_codes
            if len(received) == 1:
                self._exchange_memory[hand] = received.pop()

    def _remember_exchange_choice(
        self, position: dict[str, Any], action_id: int
    ) -> None:
        game = position["game"]
        if game.get("phase") != "ExchangingBidderCard":
            return
        if 13 <= action_id <= 36:
            self._exchange_memory[int(game["handNumber"])] = CARD_CODES[
                action_id - EXCHANGE_ACTION_OFFSET
            ]

    def _with_exchange_memory(self, position: dict[str, Any]) -> dict[str, Any]:
        hand = int(position["game"]["handNumber"])
        remembered = self._exchange_memory.get(hand)
        if remembered is None:
            return position
        augmented = dict(position)
        augmented["cfrPrivateMemory"] = {"partnersBestReceived": remembered}
        return augmented


class CommandLoop:
    def __init__(self, bot: CFRBot | None, bot_factory=None) -> None:
        self.bot = bot
        self.bot_factory = bot_factory

    def run(self) -> int:
        for raw_line in sys.stdin:
            line = raw_line.strip()
            if not line:
                continue
            try:
                command, *arguments = shlex.split(line)
                command = command.lower()
                if command == "beuci":
                    self._identify()
                elif command == "isready":
                    self._ensure_bot()
                    self._write("readyok")
                elif command == "newgame":
                    self._ensure_bot().new_game()
                elif command == "setoption":
                    self._set_option(arguments)
                elif command == "position":
                    if len(arguments) != 1:
                        raise ValueError("position requires one base64url payload")
                    self._ensure_bot().observe(decode_position(arguments[0]))
                elif command == "go":
                    self._write(self._ensure_bot().choose())
                elif command == "stop":
                    continue
                elif command == "quit":
                    if self.bot is not None:
                        self.bot.save()
                    return 0
                else:
                    raise ValueError(f"unknown-command {command}")
            except Exception as error:
                message = str(error).replace("\r", " ").replace("\n", " ")
                self._write(f"error {message}")
        if self.bot is not None:
            self.bot.save()
        return 0

    def _identify(self) -> None:
        self._write(f'id name "{ENGINE_NAME}"')
        self._write(f'id author "{ENGINE_AUTHOR}"')
        self._write("protocol bideuchre 1")
        self._write("beuciok")

    def _set_option(self, arguments: list[str]) -> None:
        lowered = [argument.lower() for argument in arguments]
        if "name" not in lowered:
            raise ValueError("setoption requires name")
        name_index = lowered.index("name")
        value_index = lowered.index("value") if "value" in lowered else len(arguments)
        name = " ".join(arguments[name_index + 1:value_index]).lower()
        value = " ".join(arguments[value_index + 1:]) if value_index < len(arguments) else ""
        if name == "deterministic":
            self._ensure_bot().deterministic = value.lower() in ("1", "true", "yes", "on")

    def _ensure_bot(self) -> CFRBot:
        if self.bot is None:
            if self.bot_factory is None:
                raise RuntimeError("engine initialization is unavailable")
            self.bot = self.bot_factory()
        return self.bot

    @staticmethod
    def _write(line: str) -> None:
        print(line, flush=True)


def default_state_dir() -> Path:
    configured = os.environ.get("BIDEUCHRE_CFR_STATE_DIR")
    return Path(configured) if configured else Path.cwd() / "engines" / "python-cfr" / "state"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=ENGINE_NAME)
    parser.add_argument("--state-dir", type=Path, default=default_state_dir())
    parser.add_argument("--rollouts", type=int, default=4)
    parser.add_argument("--seed", type=int, default=1946)
    parser.add_argument("--deterministic", action="store_true")
    parser.add_argument("--train-batch", type=int, default=4)
    options = parser.parse_args(argv)
    if options.rollouts < 1:
        parser.error("--rollouts must be at least 1")
    if options.train_batch < 1:
        parser.error("--train-batch must be at least 1")
    def create_bot() -> CFRBot:
        return CFRBot(
            options.state_dir,
            rollout_samples=options.rollouts,
            seed=options.seed,
            deterministic=options.deterministic,
            train_batch=options.train_batch,
        )

    return CommandLoop(None, create_bot).run()


if __name__ == "__main__":
    raise SystemExit(main())
