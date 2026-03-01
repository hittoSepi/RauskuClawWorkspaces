# HOLVI Infra Dedicated Base Image

## Status: COMPLETED

## Summary

Created a dedicated base image for HOLVI infra VM with correct SSH key.

## Changes Made

### Code Change
- [MainViewModel.cs:1028](../GUI/ViewModels/Main/MainViewModel.cs#L1028) - Changed disk path from `arch.qcow2` to `infra.qcow2`

### Files
- `VM/arch.qcow2` - Base image for regular workspaces (unchanged)
- `VM/system-holvi-infra/infra.qcow2` - Dedicated HOLVI infra VM with correct SSH key

## What Was Done

1. **Booted golden image** with serial console using WHPX acceleration
2. **Fixed SSH authorized_keys** for both root and rausku user:
   ```
   ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPqNQzrgLsf64WC7bsUU1nC3Tih/qG75GG3enfJUKwsg holvi-infra
   ```
3. **Cleaned cloud-init state** for fresh provisioning
4. **Created dedicated image** at `VM/system-holvi-infra/infra.qcow2`
5. **Updated code** to use `infra.qcow2` instead of `arch.qcow2`

## Key Learnings

- QEMU on Windows requires WHPX acceleration: `-machine q35,accel=whpx,kernel-irqchip=off`
- Overlay disks with backing files can cause issues - direct copy is simpler
- SSH key must be set for both root and the workspace user

## Testing

```powershell
# Start RauskuClaw and run HOLVI setup
# Then test SSH:
ssh -i D:\RauskuClaw\GUI\RauskuClaw\VM\keys\system-holvi-infra\id_ed25519 -p 6222 rausku@127.0.0.1
```

## Checklist

- [x] Boot golden image with serial console
- [x] Login with password
- [x] Fix authorized_keys (root and rausku)
- [x] Clean cloud-init state
- [x] Shutdown
- [x] Create dedicated infra.qcow2
- [x] Update code to use infra.qcow2
- [ ] Test HOLVI setup
