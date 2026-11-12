"""Measure complete transfer/forward/loss/backward/AdamW batches on this host."""
from dataclasses import dataclass
import copy
import time
import numpy as np
import torch
from .models import HybridNet, BoundaryNet


@dataclass(frozen=True)
class DeviceReport:
    device: str
    available: bool
    cpu_batches_per_second: float
    mps_batches_per_second: float | None
    reason: str


def benchmark_batch(size=32):
    return dict(features=np.zeros((size,1188),np.float32), legal=np.ones((size,61),bool),
                policy_target=np.full((size,61),1/61,np.float32), policy_weight=np.ones(size,np.float32),
                teacher_eligible=np.ones(size,bool), ownership_target=np.ones((size,24),np.int64),
                ownership_mask=np.ones((size,24),bool), trick_target=np.zeros(size,np.int64),
                win_target=np.arange(size,dtype=np.float32)%2,
                boundary_features=np.zeros((size,38),np.float32),
                boundary_win_target=np.arange(size,dtype=np.float32)%2)


def _measure(device, batch, main_state, boundary_state):
    # Local import keeps the public training/objective implementation authoritative.
    from .trainer import training_losses, memory_bytes
    main, boundary = HybridNet().to(device), BoundaryNet().to(device)
    main.load_state_dict(main_state); boundary.load_state_dict(boundary_state)
    optimizer = torch.optim.AdamW([*main.parameters(),*boundary.parameters()],lr=.0003)
    def sync():
        if device == 'mps': torch.mps.synchronize()
    def step():
        if memory_bytes() > batch.get('_max_memory_bytes', 2 << 30) - (16 << 20):
            raise MemoryError('device benchmark memory capacity')
        optimizer.zero_grad(set_to_none=True)
        raw = main(torch.tensor(batch['features'],device=device))
        losses = training_losses(raw,batch)
        boundary_logits = boundary(torch.tensor(batch['boundary_features'],device=device)).flatten()
        loss = sum(losses.values()) + torch.nn.functional.binary_cross_entropy_with_logits(
            boundary_logits,torch.tensor(batch['boundary_win_target'],device=device))
        loss.backward(); optimizer.step(); sync()
        if not torch.isfinite(loss): raise ValueError('nonfinite benchmark loss')
        return raw['win_logit'].detach().cpu(), float(loss.detach().cpu())
    first, loss = step()
    step()
    start = time.monotonic()
    for _ in range(5): step()
    elapsed = time.monotonic()-start
    result = {key:value.detach().cpu().clone() for key,value in main.state_dict().items()}
    return 5/elapsed, first, loss, result


def choose_device(requested: str, benchmark_batch: dict) -> DeviceReport:
    if requested not in ('cpu','mps','auto'):
        raise ValueError('device must be cpu, mps or auto')
    available = bool(torch.backends.mps.is_available())
    if requested == 'mps' and not available:
        raise ValueError('MPS unavailable on this Python/PyTorch/host')
    if requested == 'cpu' and not benchmark_batch:
        return DeviceReport('cpu',True,0.,None,'explicit CPU; benchmark not requested')
    batch = benchmark_batch or globals()['benchmark_batch']()
    old_threads = torch.get_num_threads()
    mps_rng = torch.mps.get_rng_state() if available else None
    try:
        torch.set_num_threads(1)
        with torch.random.fork_rng(devices=[]):
            torch.manual_seed(123)
            main = copy.deepcopy(HybridNet().state_dict())
            boundary = copy.deepcopy(BoundaryNet().state_dict())
            cpu, output, loss, weights = _measure('cpu',batch,main,boundary)
            if requested == 'cpu' or not available:
                reason='explicit CPU; measured end-to-end batches with one CPU thread' if requested=='cpu' else 'MPS unavailable; measured CPU with one thread'
                return DeviceReport('cpu',True,cpu,None,reason)
            try:
                mps, other, other_loss, other_weights = _measure('mps',batch,main,boundary)
                good = torch.allclose(output,other,atol=3e-4,rtol=3e-3) and abs(loss-other_loss)<3e-3
                good = good and all(torch.allclose(value,other_weights[key],atol=3e-4,rtol=3e-3) for key,value in weights.items())
                if not good: raise ValueError('CPU/MPS output, loss or optimizer numerical check failed')
            except (RuntimeError,ValueError,MemoryError) as exc:
                if requested == 'mps': raise ValueError(f'MPS benchmark failed: {exc}') from exc
                return DeviceReport('cpu',True,cpu,None,f'MPS rejected: {exc}')
            selected = 'mps' if requested == 'mps' or mps > cpu else 'cpu'
            return DeviceReport(selected,True,cpu,mps,'numerical checks passed; one CPU thread; measured transfer + both networks + backward + AdamW; no cross-device bitwise guarantee')
    finally:
        torch.set_num_threads(old_threads)
        if available:
            torch.mps.set_rng_state(mps_rng)
            torch.mps.empty_cache()
