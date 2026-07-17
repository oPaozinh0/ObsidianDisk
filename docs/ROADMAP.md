# ObsidianDisk — Roadmap de evolução

Planejamento das próximas funcionalidades, ordenado por **dependência técnica** (o que
destrava o quê) e não por categoria. A ideia central: **4 fundações destravam a maior
parte do resto**. Construa as fundações primeiro e cada recurso seguinte vira "só uma tela".

Legenda de esforço: **P** pequeno · **M** médio · **G** grande.
Estado atual de referência: v1.4.5.

---

## 📐 Princípios que o plano respeita

- **Dependency-free / offline / exe único** — nada de embutir LLM ou libs pesadas. "IA" =
  motor de regras + estatística sobre dados que já coletamos. LLM só como extra *opt-in*
  com chave do próprio usuário.
- **Reaproveitar a arquitetura** — `FileSystemNode`, `SortBySizeDescending`,
  `SafetyDatabase`, `TempCleaner`, `FileDeletion`, o padrão de página + `Nav_Checked`.
- **Segurança primeiro** — toda ação destrutiva mantém confirmação, dry-run e Undo.

---

## 🧱 Fase 0 — Fundações técnicas (habilitadores)

Pouco valor visível isoladamente, mas cada uma destrava vários recursos. **Fazer primeiro.**

| # | Fundação | O que é | Toca | Esforço | Destrava |
|---|----------|---------|------|---------|----------|
| 0.1 | **Snapshot leve da árvore por scan** | Ao fim do scan, gravar `snapshot-<ts>.json` com top ~200 pastas (`FullPath`+`Size`). Poucos KB. | `AppStorage`, `DiskScanner` (hook de fim) | M | Explicador, comparar scans, sugestões, timeline |
| 0.2 | **Metadados extras no scan** | Capturar `LastAccessUtc`, `CreationUtc` e `LastWriteUtc` de pastas. Hoje só há `LastWriteUtc` de arquivo. | `FileSystemNode`, `DiskScanner` | P | Pastas fantasma, arquivos por idade, regras |
| 0.3 | **Serviço de notificação nativa (toast)** | Wrapper de toast do Windows sem dependência externa (WinRT/`NotifyIcon` balloon como fallback). | novo `Services/Notifier.cs` | M | Alerta de disco cheio, regras "avisar", agendador |
| 0.4 | **Infra de tray + background** | `NotifyIcon`, opção "minimizar pra bandeja", loop de monitoramento leve. | `App.xaml.cs`, `MainWindow`, novo `Services/TrayService.cs` | M | Widget de tray, monitoramento contínuo, agendador |

> **Nota 0.2:** `LastAccessTime` no Windows às vezes vem desatualizado (o SO pode desligar o
> tracking via `NtfsDisableLastAccessUpdate`). Avisar o usuário quando o dado for pouco
> confiável e cair pra `LastWrite` como aproximação.

---

## 🔍 Fase 1 — Novas análises (alto valor, baixo esforço)

Reaproveitam a árvore do scan. Recomendo **uma página unificada "Descobertas"** com abas,
em vez de 4 itens separados no menu — menos poluição de navegação.

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Pastas fantasma / esquecidas** | Lista pastas grandes com data de modificação > 1–2 anos. Já existe o "dim old files" no mapa; virar lista acionável (abrir/mover/deletar). | 0.2 | P |
| **Top extensões desperdiçadas** | Agrupa bytes por extensão focando em lixo (`.log .tmp .iso .zip .cache`). Estende o breakdown do Overview. | — | P |
| **Caçador de artefatos de dev** | Agrega `node_modules`, `target/`, `bin/obj`, `.venv`, caches npm/pip/Docker. Reaproveita os caminhos que o `SafetyDatabase` já reconhece. | — | M |
| **Arquivos grandes por idade** | Cruza "Large Files" com `LastAccessUtc`: "500 MB que você não abre há 2 anos". | 0.2 | P |

---

## 🔎 Fase 2 — Buscas e navegação (melhora o mapa)

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Busca instantânea** | Campo de busca global filtrando a árvore já escaneada por nome/tipo (estilo Everything, mas no seu scan). | — | M |
| **"Onde foi parar meu espaço?"** | Modo guiado: drill-down automático sempre no maior filho até o culpado final em 3–4 passos. Usa `SortBySizeDescending`. | — | P |
| **Comparar duas pastas** | Vista lado a lado de duas subárvores; útil pra escolher qual cópia/backup manter. | — | M |

---

## 📊 Fase 3 — Insights e histórico

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Comparar dois scans (diff)** | Casa snapshots por caminho, calcula `deltaSize`, ordena por crescimento. "O que cresceu desde a semana passada". | 0.1 | M |
| **Alerta de disco cheio** | Notifica quando uso cruza limite (ex.: 90%) ou pela projeção "enche em ~X dias" que o History já calcula. | 0.3, 0.4 | P |
| **Exportar relatório** | PDF/HTML do estado do disco (treemap + tabelas), além do CSV atual. HTML self-contained é o caminho leve. | — | M |

---

## 🧹 Fase 4 — Melhorias de limpeza

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Perfis de limpeza** | "Conservador / Agressivo" selecionando subconjuntos de `TempCleaner.GetTargets()`, com preview do que cada um remove. | — | P |
| **Mover em vez de deletar** | Realocar arquivos grandes pra outro drive/pasta (liberar C: sem perder o arquivo). Novo método em `FileDeletion` (move + verificação). | — | M |
| **Agendador de limpeza** | Tarefa agendada do Windows chamando o modo headless (7.4) pra limpar temp/lixeira semanalmente. | 7.4, 0.3 | M |

---

## 🗂️ Fase 5 — Gestão de arquivos

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Detector de pastas vazias** | Varre a árvore por diretórios sem filhos (recursivo) e oferece remoção em lote. | — | P |
| **Arquivos órfãos / atalhos quebrados** | Resolve alvos de `.lnk`; lista os que apontam pra nada. Restos de desinstalação. | — | M |
| **Analisador de instaladores** | Acha `.exe`/`.msi` em Downloads de programas provavelmente já instalados. | — | M |
| **Compactar em vez de deletar** | Zipar pastas grandes raramente usadas (`System.IO.Compression`, já no runtime), mantendo conteúdo. | 0.2 (idade) | M |

---

## 🧠 Fase 6 — Inteligência e automação

Tudo se apoia nas fundações. Ordem interna natural: **Regras → Explicador → Sugestões.**

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Regras automáticas (if-this-then-that)** | `record CleanupRule` (pasta, tamanho min, idade min, ação=avisar/mover/deletar) persistido em `AppStorage`. `RuleEngine` roda ao fim do scan e coleta os nós que casam. Ações reaproveitam `FileDeletion` + confirmação. | 0.2 | M |
| **Explicador "por que encheu"** | Diff de snapshots → frase por template a partir de `pasta → delta`, rotulada pelo `SafetyDatabase`. Sem IA. | 0.1 | M |
| **Sugestões personalizadas** | Estatística sobre vários snapshots: pasta/extensão que sempre cresce → card que **cria uma regra já preenchida**. | 0.1, 6.1 | M |
| **(Opcional) Explicações com LLM (BYO key)** | Setting desligado por padrão; usuário cola a própria chave. Envia só os números agregados (`pasta → delta`), nunca arquivos. | 6.2 | M |

---

## 🖥️ Fase 7 — Alcance e plataforma

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Múltiplos drives** | Dashboard cobrindo todos os discos de uma vez (loop sobre `DriveInfo.GetDrives()`), não um por vez. | — | M |
| **Pastas de rede / externos** | Escanear HD externo / share UNC. Tratar latência e permissão no `DiskScanner`. | — | M |
| **Bandeja do sistema (tray)** | Ícone mostrando % de uso, monitorando em segundo plano; mini-histórico no hover. | 0.4, 0.1 | M |
| **Modo CLI / headless** | `ObsidianDisk.exe --scan C: --report out.json` (e `--clean`). Você já aceita path como argumento. **Habilita o agendador (4.3).** | — | M |
| **Menu de contexto do Explorer** | Botão direito na pasta → "Analisar com ObsidianDisk". Chave no registro apontando pro exe com o path. | — | P |

---

## 🎯 Fase 8 — Foco e experiência

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Modo "meta de limpeza"** | "Quero liberar 20 GB" → monta lista priorizada de candidatos (temp + duplicados + grandes/antigos) até bater a meta. | 1.x | M |
| **Gamificação leve** | Total acumulado de "X GB recuperados", streak de manutenção. Combina com o tom moderno. | histórico | P |
| **Timeline de espaço** | Anima o treemap evoluindo entre snapshots ("veja o disco encher em câmera rápida"). | 0.1 | G |
| **Modo apresentação / print** | Exporta screenshot bonito do treemap pra compartilhar em fórum. | — | P |

---

## 🔐 Fase 9 — Confiança e segurança

| Recurso | Como funciona | Depende de | Esforço |
|---------|---------------|-----------|---------|
| **Simulação (dry-run)** | "Mostre o que aconteceria" antes de qualquer limpeza real, com relatório. Deveria virar padrão em toda ação em lote. | — | P |
| **Lixeira interna com retenção** | Quarentena própria por N dias antes do delete definitivo, além da Recycle Bin. | — | M |
| **SafetyDatabase comunitário** | Permitir contribuição de verdicts de caminhos novos (arquivo local + PR). | — | M |

---

## 🗺️ Sequência recomendada (ondas de entrega)

1. **Onda 1 — Fundações:** 0.1 snapshot, 0.2 metadados, 0.3 notificação, 0.4 tray.
2. **Onda 2 — Ganhos rápidos e visíveis:** Fase 1 inteira (página "Descobertas"), 2.2
   drill-down guiado, 3.2 alerta de disco cheio, 4.1 perfis de limpeza, 9.1 dry-run.
   *Muito valor, esforço P/M, tudo já destravado pela Onda 1.*
3. **Onda 3 — Diferenciação:** Fase 6 (regras → explicador → sugestões), 3.1 diff de
   scans, 2.1 busca instantânea, 4.2 mover em vez de deletar.
4. **Onda 4 — Plataforma:** 7.4 CLI headless → 4.3 agendador, 7.1 múltiplos drives,
   7.3 tray completo, 7.5 menu de contexto.
5. **Onda 5 — Polimento:** Fase 5 gestão de arquivos, Fase 8 experiência, resto da Fase 9.

### Caminho crítico de dependências
```
0.2 metadados ─┬─> Pastas fantasma, Arquivos por idade
               └─> Regras automáticas ──> Sugestões
0.1 snapshot ──┬─> Explicador ──> Sugestões
               ├─> Diff de scans
               └─> Timeline
0.3 toast ─────┬─> Alerta disco cheio
0.4 tray ──────┘   └─> Tray widget, Agendador
7.4 CLI ───────────> Agendador de limpeza
```
