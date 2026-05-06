---
source_url: http://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/prediction.html
fetched: 2026-05-05
section: related-packages
package: netcode-for-entities
---

# Prediction | Netcode for Entities 6.6.0

## Overview

Prediction is a mechanism to manage latency in your game. The following topics cover prediction concepts and implementation.

### Key Topics

| Topic | Description |
|-------|-------------|
| Introduction to prediction | "Client prediction allows clients to use their own inputs to locally simulate the game, without waiting for the server's simulation result." |
| Prediction in Netcode for Entities | Guidance on implementing client prediction within the Netcode for Entities framework. |
| Prediction smoothing | The `GhostPredictionSmoothingSystem` provides "a way of reconciling and reducing prediction errors over time, to make the transitions between states smoother." |
| Prediction switching | Netcode supports "opting into prediction on a per-client, per-ghost basis, based on some criteria (for example, predict all ghosts inside this radius of my clients' character controller)." |
| Prediction edge cases and known issues | Important considerations and edge cases when implementing client-side prediction. |

---

## Outgoing Hyperlinks

- `intro-to-prediction.html` — Introduction to prediction
- `prediction-n4e.html` — Prediction in Netcode for Entities
- `prediction-smoothing.html` — Prediction smoothing
- `prediction-switching.html` — Prediction switching
- `prediction-details.html` — Prediction edge cases and known issues
