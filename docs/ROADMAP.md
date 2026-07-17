# ObsidianDisk — Roadmap de evolução

Status vivo do plano. Legenda: ✅ entregue · 🔶 parcial · ⬜ pendente.
Esforço: **P** pequeno · **M** médio · **G** grande.

---

## 📍 Estado atual (para retomar em um chat novo)

- **Versão publicada:** **v1.7.0** (partiu da v1.4.5). Releases em https://github.com/oPaozinh0/ObsidianDisk/releases
- **Fluxo git:** `feature/* → PR → debug → PR de release → main`. `main` e `debug` costumam ficar alinhadas após cada release.
  - Features nascem de `debug`, viram PR para `debug`. Release = PR `debug → main` + bump de versão + tag.
- **CI bloqueado (billing):** o `release.yml` (dispara em tags `v*`) **não roda**. Releases são **manuais**:
  1. `dotnet publish ObsidianDisk.csproj -c Release` → `bin/Release/net8.0-windows/win-x64/publish/ObsidianDisk.exe` (~72 MB, single-file).
  2. `gh release create vX.Y.Z "<exe>" --target main --title "ObsidianDisk vX.Y.Z" --notes "..."` (notas **em inglês**).
- **Convenções:** strings em **5 idiomas** (pt/en/es/fr/de, `Resources/Strings.*.xaml`); cabeçalhos com botões **declarados primeiro** no DockPanel (evita overflow em telas estreitas); commits/PRs **sem coautoria do Claude**; verificar features de runtime rodando o app (o exe Debug pode ficar travado se o app estiver aberto — o publish Release usa outro caminho).

### Fundações técnicas já construídas (reaproveitar)
- `Services/SnapshotStore` — snapshot leve da árvore por scan (top pastas). API: `Capture`, `ListFiles`, `Load`, `LoadRecent`, `RecentForPath`, `TwoLatestForPath`, `Diff`.
- `Services/DiskForecaster` — regressão/projeção de esgotamento (usado por History e alerta).
- `Services/Notifier` + `Services/TrayService` — bandeja + notificações nativas (WinForms, sem NuGet).
- `Services/DiscoveryAnalyzer` — análises sobre a árvore (fantasma, extensões, dev junk, por idade, vazias).
- `Services/GrowthExplainer`, `Services/SuggestionEngine`, `Services/RuleEngine` — Fase 6 (inteligência).
- `Models/FileSystemNode` — captura `LastWriteUtc`/`LastAccessUtc`/`CreationUtc`; helper `FindByPath`.
- `Services/AppStorage` — settings, history, `rules.json`. `App.xaml` tem `DarkTextBox`/`DarkCombo`/`DarkCheck`.

---

## ✅ Entregue por versão

- **v1.5.0** — Página **Descobertas** (pastas fantasma, extensões, dev junk, arquivos por idade), **alerta de disco cheio**, **bandeja + notificações**, **nova logo**, snapshots + metadados.
- **v1.6.0** — **Perfis de limpeza** (conservador/agressivo), **drill-down guiado** no mapa, **comparar scans** ("O que mudou"), **explicador "por que encheu?"**.
- **v1.7.0** — **Regras automáticas**, **sugestões personalizadas**, **detector de pastas vazias**, **simulação (dry-run)** na limpeza, fix de overflow dos cabeçalhos.

---

## 🧱 Fase 0 — Fundações · ✅ COMPLETA
0.1 Snapshot ✅ · 0.2 Metadados ✅ · 0.3 Notificação ✅ · 0.4 Bandeja ✅

## 🔍 Fase 1 — Novas análises · ✅ COMPLETA
Pastas fantasma ✅ · Top extensões ✅ · Dev junk ✅ · Arquivos grandes por idade ✅ · (bônus) Pastas vazias ✅

## 🔎 Fase 2 — Buscas e navegação · 🔶
| Recurso | Status | Esforço |
|---|---|---|
| Drill-down guiado ("onde foi parar?") | ✅ | — |
| **Busca instantânea no mapa** (nome/tipo, estilo Everything no scan) | ⬜ | M |
| Comparar duas pastas (lado a lado) | ⬜ | M |

## 📊 Fase 3 — Insights e histórico · 🔶
| Recurso | Status | Esforço |
|---|---|---|
| Comparar dois scans ("O que mudou") | ✅ | — |
| Alerta de disco cheio | ✅ | — |
| Exportar relatório (PDF/HTML, além do CSV) | ⬜ | M |

## 🧹 Fase 4 — Limpeza · 🔶
| Recurso | Status | Esforço |
|---|---|---|
| Perfis de limpeza (conservador/agressivo) | ✅ | — |
| **Mover em vez de deletar** (realocar p/ outro drive) | ⬜ | M |
| Agendador de limpeza (Task Scheduler + CLI headless) | ⬜ | M (depende do CLI, 7.4) |

## 🗂️ Fase 5 — Gestão de arquivos · 🔶
| Recurso | Status | Esforço |
|---|---|---|
| Detector de pastas vazias | ✅ | — |
| Arquivos órfãos / atalhos `.lnk` quebrados | ⬜ | M |
| **Analisador de instaladores** em Downloads (novo modo em Descobertas) | ⬜ | P |
| Compactar em vez de deletar (zip de pastas raras) | ⬜ | M |

## 🧠 Fase 6 — Inteligência · ✅ COMPLETA
Regras automáticas ✅ · Explicador "por que encheu?" ✅ · Sugestões personalizadas ✅
- Extra opcional futuro: explicações com **LLM (BYO key)** — desligado por padrão, só envia números agregados. ⬜

## 🖥️ Fase 7 — Alcance e plataforma · 🔶
| Recurso | Status | Esforço |
|---|---|---|
| Bandeja do sistema | ✅ (tooltip + menu + minimizar) | — |
| **Múltiplos drives** (dashboard de todos os discos) | ⬜ | G |
| Ler pastas de rede / externos (UNC) | ⬜ | M |
| Modo CLI / headless (`--scan C: --report out.json`) | ⬜ | M |
| Menu de contexto do Explorer ("Analisar com ObsidianDisk") | ⬜ | P |

## 🎯 Fase 8 — Experiência · ⬜
| Recurso | Status | Esforço |
|---|---|---|
| Modo "meta de limpeza" ("quero liberar 20 GB") | ⬜ | M |
| Gamificação leve (total de GB recuperados, streak) | ⬜ | P |
| Timeline de espaço (treemap animado entre snapshots) | ⬜ | G |
| Modo apresentação / print do treemap | ⬜ | P |

## 🔐 Fase 9 — Confiança e segurança · 🔶
| Recurso | Status | Esforço |
|---|---|---|
| Simulação (dry-run) na limpeza | ✅ | — |
| Lixeira interna com retenção (quarentena N dias) | ⬜ | M |
| SafetyDatabase comunitário (contribuir verdicts) | ⬜ | M |

---

## 🗺️ Próximos passos sugeridos (para o próximo chat)

1. **Busca instantânea no mapa** (Fase 2) — recomendada; alto valor, contida. Campo que filtra a árvore já escaneada por nome/tipo.
2. **Analisador de instaladores** (Fase 5) — ganho rápido: mais um modo em `DiscoveryAnalyzer`/`DiscoveriesPage` (padrão já pronto).
3. **Mover em vez de deletar** (Fase 4) — realocar grandes p/ outro drive; novo método em `FileDeletion` + fluxo de confirmação.
4. **Modo meta de limpeza** (Fase 8) ou **exportar relatório HTML** (Fase 3) — quando quiser variar.

Acumular algumas features na `debug` e cortar a **v1.8.0** quando houver massa crítica.
