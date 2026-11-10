"""Compact feed-forward networks; stored layers always emit raw logits."""
import torch
from torch import nn
from . import schema

class HybridNet(nn.Module):
    def __init__(self):
        super().__init__()
        self.fc1 = nn.Linear(schema.FEATURE_COUNT,256)
        self.fc2 = nn.Linear(256,128)
        self.policy = nn.Linear(128,61)
        self.win = nn.Linear(128,1)
        self.trick = nn.Linear(128,7)
        self.ownership = nn.Linear(128,96)

    def forward(self, features):
        if features.ndim < 1 or features.shape[-1] != 1188:
            raise ValueError('HybridNet requires 1188 features')
        x = torch.relu(self.fc2(torch.relu(self.fc1(features))))
        return {'policy_logits':self.policy(x),'win_logit':self.win(x),
                'trick_logits':self.trick(x),'ownership_logits':self.ownership(x)}

class BoundaryNet(nn.Module):
    def __init__(self):
        super().__init__()
        self.fc1 = nn.Linear(schema.BOUNDARY_FEATURE_COUNT,64)
        self.fc2 = nn.Linear(64,32)
        self.out = nn.Linear(32,1)

    def forward(self, features):
        if features.ndim < 1 or features.shape[-1] != 38:
            raise ValueError('BoundaryNet requires 38 features')
        return self.out(torch.relu(self.fc2(torch.relu(self.fc1(features)))))

def normalize_output(raw, legal):
    """Empty legal masks yield zero policy; invalid scores raise for fallback."""
    if any(not torch.isfinite(x).all() for x in raw.values()):
        raise ValueError('nonfinite network output')
    logits = raw['policy_logits']
    if legal.dtype != torch.bool or legal.shape != logits.shape:
        raise ValueError('legal mask must be bool and match policy logits')
    masked = logits.masked_fill(~legal,-torch.inf)
    masked = torch.where(legal.any(dim=-1,keepdim=True),masked,torch.zeros_like(masked))
    policy = torch.softmax(masked,dim=-1).masked_fill(~legal,0.)
    ownership = raw['ownership_logits']
    return {'policy':policy,'win':torch.sigmoid(raw['win_logit']),
            'trick':torch.softmax(raw['trick_logits'],dim=-1),
            'ownership':torch.softmax(ownership.reshape(*ownership.shape[:-1],24,4),dim=-1).reshape_as(ownership)}
