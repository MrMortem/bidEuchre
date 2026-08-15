"""Compact PyTorch network and masked CFR strategy helpers."""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    import torch

from .actions import ACTION_COUNT
from .encoding import FEATURE_DIM

MODEL_SCHEMA_VERSION = 1


def require_torch():
    try:
        import torch
    except ImportError as error:
        raise RuntimeError(
            "PyTorch is required. Run engines/python-cfr/install.sh first."
        ) from error
    return torch


def build_model(hidden_size: int = 192):
    """Build independent regret, average-strategy, and value heads."""
    torch = require_torch()

    class CFRNetwork(torch.nn.Module):
        def __init__(self) -> None:
            super().__init__()
            self.trunk = torch.nn.Sequential(
                torch.nn.Linear(FEATURE_DIM, hidden_size),
                torch.nn.LayerNorm(hidden_size),
                torch.nn.GELU(),
                torch.nn.Linear(hidden_size, hidden_size),
                torch.nn.GELU(),
            )
            self.regret_head = torch.nn.Linear(hidden_size, ACTION_COUNT)
            self.strategy_head = torch.nn.Linear(hidden_size, ACTION_COUNT)
            self.value_head = torch.nn.Sequential(
                torch.nn.Linear(hidden_size, 1),
                torch.nn.Tanh(),
            )

        def forward(self, features):
            hidden = self.trunk(features)
            return (
                self.regret_head(hidden),
                self.strategy_head(hidden),
                self.value_head(hidden).squeeze(-1),
            )

    return CFRNetwork()


def legal_mask(action_ids: list[int], *, device=None):
    torch = require_torch()
    mask = torch.zeros(ACTION_COUNT, dtype=torch.bool, device=device)
    if action_ids:
        mask[action_ids] = True
    return mask


def regret_matching(regrets, mask):
    """Return exact regret-matching probabilities over the legal actions."""
    torch = require_torch()
    positive = torch.relu(regrets) * mask.to(dtype=regrets.dtype)
    total = positive.sum(dim=-1, keepdim=True)
    legal = mask.to(dtype=regrets.dtype)
    uniform = legal / legal.sum(dim=-1, keepdim=True).clamp_min(1.0)
    return torch.where(total > 1e-12, positive / total.clamp_min(1e-12), uniform)


def masked_average_strategy(logits, mask):
    torch = require_torch()
    masked = logits.masked_fill(~mask, torch.finfo(logits.dtype).min)
    return torch.softmax(masked, dim=-1)
