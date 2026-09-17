# NetToCxSim ⚡

> **Omron CX-Simulator Ethernet FINS TCP/UDP Bridge & SCADA Simulator**  
> *Developed by **ismaillowkey** | Version: **v0.3.0** [x86 Release]*

[![Platform](https://img.shields.io/badge/Platform-Windows%20(x86%2032--bit)-blue.svg)](https://github.com)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-purple.svg)](https://dotnet.microsoft.com)
[![Protocol](https://img.shields.io/badge/Protocol-Omron%20FINS%20(TCP%2FUDP)-green.svg)](https://www.ia.omron.com)
[![License](https://img.shields.io/badge/License-MIT-orange.svg)](LICENSE)
[![Support](https://img.shields.io/badge/Support-Saweria-yellow.svg)](https://saweria.co/ismaillowkey)

---

## 🌐 Language / Bahasa
* [🇮🇩 Bahasa Indonesia](#-panduan-bahasa-indonesia)
* [🇬🇧 English Guide](#-english-guide)

---

# 🇮🇩 Panduan Bahasa Indonesia

### 📌 Tentang NetToCxSim
**NetToCxSim** adalah aplikasi bridge jembatan jaringan ringan (32-bit) yang menghubungkan **Omron CX-Simulator (`CxCpuMain.exe`)** langsung ke jaringan Ethernet nyata melalui protokol standar **Omron FINS (TCP & UDP port 9600)**.

Dengan NetToCxSim, Anda dapat mensimulasikan dan menguji komunikasi antara ladder program di **CX-Programmer** dengan perangkat lunak luar seperti **HMI (EasyBuilder Pro, Haiwell)**, **SCADA (Ignition, Kepware, Wonderware)**, **Node-RED**, **Python**, atau script **C#** secara **100% virtual tanpa PLC fisik ataupun modul hardware Ethernet**.

---

### 🌟 Fitur Utama
1. **Direct Process Memory Scanner**:
   - Membaca dan menulis secara instan ke memory proses `CxCpuMain.exe` (CX-Simulator) dengan latensi sub-milidetik.
   - Mendukung area memori PLC lengkap: **CIO**, **Work (W)**, **Holding (H)**, **Data Memory (DM / D)**, **Auxiliary (A)**, **Timer (T)**, dan **Counter (C)**.

2. **FINS TCP & UDP Server (Port :9600)**:
   - Mendukung standar FINS Command:
     - `01 01` : Memory Area Read (Bit & Word)
     - `01 02` : Memory Area Write (Bit & Word)
   - Multi-client concurrency (bisa dihubungkan ke HMI, SCADA, dan logger sekaligus).
   - **Port Auto-Fallback**: Jika port 9600 sedang digunakan, otomatis mencoba port 9601, 9602, dst.

3. **Built-in Workstation & SCADA Simulator**:
   - **Digital Rack I/O**: 13 Toggle Switch Digital Inputs (CIO 0.00 - 0.12) & 8 Pilot Lamps Outputs (CIO 100.00 - 100.07).
   - **Filling Water Tank Simulator**: Visualisasi tangki air 2D/3D interaktif lengkap dengan inlet pump, inlet valve, drain valve, float switch low (`CIO 0.02`), float switch high (`CIO 0.03`), dan register level analog (`DM 10`).

4. **Mode Simple vs Advanced**:
   - **Simple Mode**: Tampilan bersih dan fokus pada simulasi SCADA/proses.
   - **Advanced Mode**: Membuka tab **Inspector & Log** yang dilengkapi fitur Read/Write address register PLC secara langsung serta pemantau live log trafik FINS.

5. **Topologi Alur Data Visual**:
   - Dilengkapi diagram arsitektur interaktif yang menjelaskan hubungan data antara IDE, simulator, bridge, dan HMI/SCADA.

---

### 🔄 Arsitektur Alur Data

```
┌─────────────────┐
│  CX-Programmer  │ (Ladder Logic Editor)
└────────┬────────┘
         │  ▼ Simulation / Work Online (Ctrl+Shift+W)
┌────────┴────────┐
│  CX-Simulator   │ (CxCpuMain.exe - Virtual PLC Engine)
└────────┬────────┘
         │  ◄═══ Direct RAM / Shared Memory (CIO, W, DM, H) ═══►
┌────────┴────────┐
│   NetToCxSim    │ (FINS TCP/UDP Bridge Hub :9600)
└────────┬────────┘
         │  ◄═══ FINS Ethernet LAN / Localhost (Node 1 ⇄ Node 10) ═══►
┌────────┴────────┐
│    HMI / OPC    │ (EasyBuilder Pro, Haiwell, Ignition, Node-RED, Python)
└─────────────────┘
```

---

### 📋 7 Langkah Alur Kerja (Workflow)
1. Buat program ladder di **CX-Programmer**.
2. Jalankan simulasi: Menu **Simulation** ➔ **Work Online Simulator** `(Ctrl+Shift+W)`.
3. Jalankan aplikasi **NetToCXSim** ini.
4. Jika mengubah ladder: **Stop simulator**, lalu ulangi **Langkah 2**.
5. Jika muncul pesan error koneksi: Menu **PLC** ➔ **Transfer** ➔ **To PLC...**
6. Ubah mode: Menu **PLC** ➔ **Operating Mode** ➔ **Monitor** `(Ctrl+M)`.
7. Aktifkan monitoring: Menu **PLC** ➔ **Monitor** ➔ **Monitoring**.

---

### 📱 Panduan Koneksi HMI & SCADA

| Parameter | Pengaturan Rekomendasi |
| :--- | :--- |
| **Device Type / Driver** | Omron **CP1L/H**, **CP1E**, atau **CJ/CS** |
| **Protokol** | **FINS (TCP atau UDP)** |
| **IP Address** | `127.0.0.1` (jika di komputer yang sama) atau IP LAN komputer |
| **Port** | `9600` *(atau sesuaikan jika fallback ke 9601/9602)* |
| **PLC Node ID** | `1` |
| **HMI Node ID** | `10` |
| **Unit Number** | `0` |
| **Network Number** | `0` |

> 💡 **Catatan untuk Haiwell Cloud SCADA**:  
> Gunakan tipe tag **`DW`** (bukan `D`) untuk menulis nilai Word 16-bit ke Data Memory (DM)!

---

### 🛠️ Kompilasi & Build dari Source Code
Pastikan Anda memiliki **.NET SDK** terpasang di sistem:
```bash
# Clone repository
git clone https://github.com/ismaillowkey/Omron-CXSimulatorBridge.git
cd Omron-CXSimulatorBridge

# Build Solution
dotnet build NetToCXSim.sln -c Release

# Publish 32-bit x86 Release
dotnet publish src/NetToCXSim.Wpf/NetToCXSim.Wpf.csproj -c Release -r win-x86 --self-contained false -o ./publish_x86

# Atau jalankan batch script build otomatis:
publish_x86.bat
```

---
---

# 🇬🇧 English Guide

### 📌 About NetToCxSim
**NetToCxSim** is a lightweight, 32-bit network bridge utility that seamlessly connects **Omron CX-Simulator (`CxCpuMain.exe`)** to real Ethernet networks using the standard **Omron FINS protocol (TCP & UDP port 9600)**.

It allows automation engineers, students, and developers to test communication between ladder diagrams running in **CX-Programmer** and external systems such as **HMI (Weintek EasyBuilder Pro, Haiwell)**, **SCADA (Ignition, Kepware, Wonderware)**, **Node-RED**, **Python**, or custom **C#** applications **100% virtually without requiring physical PLC hardware or Ethernet modules**.

---

### 🌟 Key Features
1. **Direct Memory Scanner**:
   - Real-time sub-millisecond read/write access to the virtual PLC memory of `CxCpuMain.exe`.
   - Full support for all Omron memory areas: **CIO**, **Work (W)**, **Holding (H)**, **Data Memory (DM/D)**, **Auxiliary (A)**, **Timers (T)**, and **Counters (C)**.

2. **FINS TCP & UDP Server (Port 9600)**:
   - Compliant with standard FINS Commands:
     - `01 01` : Memory Area Read (Bit & Word)
     - `01 02` : Memory Area Write (Bit & Word)
   - Supports concurrent multi-client connections.
   - **Port Auto-Fallback**: Automatically shifts to 9601, 9602, etc. if port 9600 is occupied.

3. **Integrated SCADA Workstations**:
   - **Digital Rack I/O**: 13 toggle switches (CIO 0.00 - 0.12) & 8 pilot lamps (CIO 100.00 - 100.07).
   - **Water Tank Filling Simulator**: 2D/3D physics-based simulation with inlet pump, inlet valve, drain valve, low level float switch (`CIO 0.02`), high level float switch (`CIO 0.03`), and analog level feedback (`DM 10`).

4. **Simple vs Advanced Modes**:
   - **Simple Mode**: Minimalist interface focused purely on simulation and testing.
   - **Advanced Mode**: Expands the **Inspector & Log** tab featuring custom variable inspector (Read/Toggle/Write) and real-time FINS communication logs.

5. **Visual Architecture Diagram**:
   - Embedded interactive diagram illustrating data flow between IDE, virtual engine, bridge, and SCADA clients.

---

### 🔄 Data Architecture Pipeline

```
┌─────────────────┐
│  CX-Programmer  │ (Ladder Logic Editor)
└────────┬────────┘
         │  ▼ Simulation / Work Online (Ctrl+Shift+W)
┌────────┴────────┐
│  CX-Simulator   │ (CxCpuMain.exe - Virtual PLC Engine)
└────────┬────────┘
         │  ◄═══ Direct RAM / Shared Memory Scan (CIO, W, DM, H) ═══►
┌────────┴────────┐
│   NetToCxSim    │ (FINS TCP/UDP Bridge Hub :9600)
└────────┬────────┘
         │  ◄═══ FINS Ethernet LAN / Localhost (Node 1 ⇄ Node 10) ═══►
┌────────┴────────┐
│    HMI / OPC    │ (EasyBuilder Pro, Haiwell, Ignition, Node-RED, Python)
└─────────────────┘
```

---

### 📋 Synchronization Workflow
1. Open or create your ladder diagram in **CX-Programmer**.
2. Start the simulation: **Simulation** ➔ **Work Online Simulator** `(Ctrl+Shift+W)`.
3. Launch the **NetToCXSim** bridge application.
4. If modifying the ladder: **Stop simulator**, edit ladder, then repeat **Step 2**.
5. If communication errors occur: **PLC** ➔ **Transfer** ➔ **To PLC...**
6. Set mode: **PLC** ➔ **Operating Mode** ➔ **Monitor** `(Ctrl+M)`.
7. Turn monitoring on: **PLC** ➔ **Monitor** ➔ **Monitoring**.

---

### 📱 HMI & SCADA Configuration

| Parameter | Recommended Setting |
| :--- | :--- |
| **Device / Driver** | Omron **CP1L/H**, **CP1E**, or **CJ/CS** |
| **Protocol** | **FINS (TCP or UDP)** |
| **IP Address** | `127.0.0.1` (localhost) or host machine LAN IP |
| **Port** | `9600` *(or fallback port if 9600 is taken)* |
| **PLC Node ID** | `1` |
| **HMI Node ID** | `10` |
| **Unit Number** | `0` |
| **Network Number** | `0` |

> 💡 **Tip for Haiwell Cloud SCADA**:  
> Use tag type **`DW`** (instead of `D`) when writing 16-bit word values to Data Memory (DM)!

---

### 🛠️ Building from Source
Ensure you have the **.NET SDK** installed on Windows:
```bash
# Clone the repository
git clone https://github.com/ismaillowkey/Omron-CXSimulatorBridge.git
cd Omron-CXSimulatorBridge

# Build the solution
dotnet build NetToCXSim.sln -c Release

# Publish 32-bit x86 Release
dotnet publish src/NetToCXSim.Wpf/NetToCXSim.Wpf.csproj -c Release -r win-x86 --self-contained false -o ./publish_x86

# Or use the one-click build script:
publish_x86.bat
```

---

### ☕ Support & Donations
If you find this software helpful for your work, projects, or studies, consider supporting the developer:
- ☕ **Saweria:** [https://saweria.co/ismaillowkey](https://saweria.co/ismaillowkey)

---

### 📄 License
This project is licensed under the **MIT License**.
You are free to use, modify, and distribute this software for personal, educational, and commercial simulation purposes.
