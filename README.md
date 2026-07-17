<div align="center">

# 🔮 ObsidianDisk

**Gerenciador visual de espaço em disco para Windows**

Descubra o que está ocupando seu disco, explore num treemap interativo em tempo real,
encontre arquivos gigantes e duplicados, e libere espaço — tudo num app só.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-7C5CFF?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Windows-3B82F6?logo=windows&logoColor=white)](#)
[![Licença MIT](https://img.shields.io/badge/Licen%C3%A7a-MIT-22C55E)](LICENSE)
[![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-14B8A6?logo=windows11&logoColor=white)](#)

<img src="docs/screenshot-overview.png" alt="ObsidianDisk — Visão Geral" width="850"/>

</div>

---

## ✨ Recursos

- **🏠 Visão Geral** — painel com uso do disco, blocos semânticos (Programas, Jogos, Windows, Usuários, Arquivos Temp…) e espaço por categoria de arquivo
- **🗺️ Mapa de Espaço** — treemap *squarified* estilo SpaceSniffer que se monta **ao vivo durante o scan**; duplo clique aprofunda, breadcrumb navega, tooltip mostra detalhes, filtros por categoria e por arquivos antigos
- **📄 Arquivos Grandes** — os maiores arquivos do disco, filtráveis por tamanho mínimo, com exclusão direta
- **👯 Duplicados** — detecção em 3 estágios (tamanho → hash parcial → SHA-256 completo); mantém a cópia mais recente e mostra quanto dá para recuperar
- **🧹 Limpeza** — um clique para limpar Temp do usuário, Temp do Windows, cache do Windows Update, miniaturas, relatórios de erro e Lixeira
- **📈 Histórico** — cada scan vira um registro persistente: gráfico de evolução, estatísticas, tendência de crescimento, projeção de esgotamento do disco e exportação CSV
- **🗑️ Exclusão segura** — para a Lixeira (com desfazer) ou permanente, sempre com confirmação clara
- **🌙 Interface moderna** — tema escuro completo, janela sem bordas com barra de título personalizada, tudo num único `.exe` sem dependências

## 📥 Download

Baixe o `.exe` mais recente na página de [**Releases**](../../releases) — não precisa instalar nada (nem o .NET): é um executável único e autossuficiente.

> 💡 Dica: `ObsidianDisk.exe "C:\alguma\pasta"` abre o app já escaneando o caminho informado.

## 🔧 Compilar do código-fonte

Requisitos: [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0) no Windows.

```powershell
git clone https://github.com/oPaozinh0/ObsidianDisk.git
cd ObsidianDisk
dotnet publish -c Release
# exe gerado em: bin\Release\net8.0-windows\win-x64\publish\ObsidianDisk.exe
```

## 🏗️ Arquitetura

| Componente | Papel |
|---|---|
| `Services/DiskScanner` | Varredura paralela com propagação atômica de tamanhos (renderização ao vivo) |
| `Controls/TreemapControl` | Layout *squarified* + renderização direta via `OnRender` |
| `Services/DuplicateFinder` | Detecção de duplicados em 3 estágios com hashing paralelo |
| `Services/TempCleaner` | Medição e limpeza dos locais temporários do Windows |
| `Services/SemanticGrouper` | Classifica pastas do disco em blocos semânticos |
| `Views/*` | Páginas do painel (WPF, tema escuro próprio) |

## 🤝 Contribuindo

Issues e pull requests são bem-vindos! Se encontrou um bug ou tem uma ideia, [abra uma issue](../../issues).

## ☕ Apoie o projeto

Se o ObsidianDisk te ajudou a recuperar uns gigas, considere pagar um café:

<a href="https://www.buymeacoffee.com/oPaozinh0" target="_blank">
  <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="50"/>
</a>

## 📄 Licença

Distribuído sob a licença [MIT](LICENSE).
