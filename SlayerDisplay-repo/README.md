[README.md](https://github.com/user-attachments/files/33831060/README.md)
# SlayerDisplay

Serviço de substituição para o software **SLAYER CPU Live Temperature** (NTECH) que faz o display do cooler **NTECH Slayer RXA50** voltar a mostrar a temperatura do CPU em processadores recentes, como o AMD Ryzen 9000 (Zen 5).

> 🇬🇧 **English summary:** The official NTECH "SLAYER CPU Live Temperature" v1.0.0 service can't read sensors on newer CPUs (e.g. Ryzen 9000 / Zen 5), so the RXA50 display only shows dashes (`---`). SlayerDisplay is a drop-in Windows service that reads sensors with LibreHardwareMonitor and sends them to the display through NTECH's own `TempComm.dll`. See [Instalação](#instalação) and run `install.ps1` as administrator.

---

## O problema

**Sintomas:**

- O display do Slayer RXA50 mostra apenas traços (`--`).
- O serviço `SLAYERCPULiveTemperature` aparece como "Em execução".
- Reinstalar, reiniciar o serviço, desligar o Armoury Crate, desligar a Integridade de memória ou fazer power-drain ao PC não resolve.
- Com `IsLog=True` no `config.ini`, o log enche-se de:

  ```
  XKWLib.XKWHardware.GetCpuRunInfo:
      System.Exception: CPU clock 信息获取失败,权限不够
  ```
  ("falha ao obter o clock do CPU, permissões insuficientes")

- O Windows Defender pode também detetar o `XKWHardwareLib.sys` como `HackTool:Win32/Winring0`.

**A causa:**

O software oficial (v1.0.0, DLLs de maio de 2024) usa um fork antigo do *OpenHardwareMonitor* para ler os sensores. Esse fork não conhece CPUs lançados depois dessa data, como a família AMD `1Ah` (Zen 5, Ryzen 9000).

Em `XKWHardware.GetCpuRunInfo()`, o software procura sensores de clock por core (`Core #1`, `Core #2`…). Num CPU que não conhece, esses sensores não existem. O código lança então uma exceção com a mensagem genérica "permissões insuficientes", e o nome **não corresponde à causa real**: o driver está carregado e o serviço corre como `LocalSystem`. Como não há dados do CPU, nada chega ao display e aparecem os traços.

Ou seja, não é problema de permissões, drivers, portas USB nem conflitos com outro software. O software oficial simplesmente não suporta o CPU.

## A solução

O software original divide-se em duas partes:

| Componente | Função | Problema |
|---|---|---|
| `XKWHardwareLib.dll` + `XKWHardwareLib.sys` | Lê os sensores (fork do OHM + driver WinRing0) | Não suporta CPUs recentes; o driver é bloqueado pelo Defender |
| `TempComm.dll` (nativo) | Comunica por USB com o display | Funciona bem |

O **SlayerDisplay** mantém o `TempComm.dll` original e troca a leitura de sensores pelo [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), que é mantido e suporta CPUs atuais. As versões recentes do LibreHardwareMonitor usam o driver [PawnIO](https://pawnio.eu/) em vez do WinRing0, por isso o problema do Defender também desaparece.

## Requisitos

- Windows 10/11
- O software original **SLAYER CPU Live Temperature** instalado, porque é de lá que vem o `TempComm.dll`
- [PawnIO](https://pawnio.eu/), o driver usado pelo LibreHardwareMonitor para ler os sensores:
  ```powershell
  winget install namazso.PawnIO
  ```

## Instalação

### A partir de uma release (recomendado)

1. Descarrega o `SlayerDisplay-win-x86.zip` da página [Releases](https://github.com/lizandro005/SlayerDisplay-Repo/releases) e extrai-o.
2. Abre o PowerShell **como administrador** nessa pasta e corre:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

O script:
- para e desativa o serviço original `SLAYERCPULiveTemperature`;
- copia os ficheiros para `C:\Program Files\SlayerDisplay`, incluindo o `TempComm.dll` da instalação original;
- regista e arranca o serviço `SlayerDisplay`, que arranca automaticamente e reinicia sozinho se falhar.

### A partir do código-fonte

Requer o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
git clone https://github.com/lizandro005/SlayerDisplay-Repo.git
cd SlayerDisplay-Repo
dotnet publish src/SlayerDisplay -c Release -o publish

# teste em consola (como administrador, com o serviço original parado)
Stop-Service SLAYERCPULiveTemperature
Copy-Item 'C:\Program Files (x86)\SLAYER CPU Live Temperature\TempComm.dll' publish\
.\publish\SlayerDisplay.exe      # Ctrl+C para sair

# instalar como serviço
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

### Depois de instalar (opcional)

Depois de reiniciar o PC e confirmar que o display funciona, podes:
- desinstalar o "SLAYER CPU Live Temperature" nas Aplicações instaladas, o que também remove o driver WinRing0 (o `TempComm.dll` já foi copiado para a pasta do SlayerDisplay);
- remover qualquer exclusão do Defender que tenhas criado para a pasta do Slayer.

## Desinstalação

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

O script remove o serviço e a pasta e volta a ativar o serviço original, se este ainda estiver instalado.

## Resolução de problemas

Os avisos do serviço ficam no **Visualizador de Eventos → Registos do Windows → Aplicação**, com a origem `SlayerDisplay`. Também podes correr `C:\Program Files\SlayerDisplay\SlayerDisplay.exe` numa consola de administrador, com o serviço parado, para ver o output diretamente.

| Mensagem | Causa provável |
|---|---|
| `Sem temperatura do CPU` | O PawnIO não está instalado, ou o programa não está a correr como administrador/serviço |
| `DeviceOpen falhou` | O serviço original continua a correr, ou o cabo USB interno do cooler não está ligado à motherboard |
| `DllNotFoundException: TempComm.dll` | O `TempComm.dll` não está na pasta do executável |
| `BadImageFormatException` | Arquitetura do `TempComm.dll` diferente da esperada (x86); ver abaixo |

## Detalhes técnicos

Valores obtidos a partir do `XKWLib.dll` original:

- **Display USB:** VID `0x2C65`, PID `0x1001` (registado com `SetCheckUsb(bytes, 4, 20)`, seguido de `DeviceOpen(1)`)
- **Ciclo de envio:** a cada 500 ms
  - `SetDeviceTime`: 9 bytes `[século, ano%100, mês, dia, dia_semana, hora, min, seg, ms/10]`
  - `SetGpuDynamicInfo`: 8 bytes por GPU `[load, temp, 0…]`
  - `SetCpuDynamicInfo`: 8 bytes por entrada `[load, temp, clock_lo, clock_hi, 0, 0, 0, 0]`; a primeira entrada é o package (temperatura Tctl/Tdie, carga total, clock médio) e as restantes são uma por core
  - `SetMemDynamicInfo`: 8 bytes `[%usada, 0…]`
- O `TempComm.dll` é nativo de 32 bits, por isso o serviço é compilado para `win-x86`.

Testado com: AMD Ryzen 5 9600X, Windows 11.

O serviço deve funcionar com outros modelos que usem o mesmo software. Os VID/PID conhecidos no `XKWLib` são:

| Serviço original | VID | PID |
|---|---|---|
| SLAYERCPULiveTemperature | `0x2C65` | `0x1001` |
| XKWHardwareServices | `0x12B7` | `0x1001` |
| SystemTemperatureMonitoring | `0x0145` | `0x1001` |
| ZALMAN-CTM | `0x1C57` | `0x3ED1`, `0x4B8F`, `0x3A7F` |

Para outro modelo, altera `VidPid` em `DisplayWorker.cs`. Alguns destes modelos usam um formato de envio ligeiramente diferente (`SendDynamicDataNew`), por isso podem precisar de ajustes.

## Créditos

- **[Lizandro](https://github.com/lizandro005)**: diagnóstico no hardware real, testes e manutenção do projeto.
- **[Claude](https://claude.ai) (Anthropic)**: análise do código decompilado do software original, identificação da causa e do protocolo do display, e escrita do serviço, dos scripts e desta documentação.
- **[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)** e **[PawnIO](https://pawnio.eu/)**: leitura dos sensores.

## Aviso legal

Projeto independente, sem qualquer afiliação com a NTECH. O `TempComm.dll` é propriedade da NTECH e **não é distribuído** neste repositório: é copiado da tua instalação do software oficial. Usa por tua conta e risco.

## Licença

[MIT](LICENSE)
