# Automatización Y15

Esta es una aplicación de consola desarrollada en C# (.NET) diseñada para correr como un servicio en segundo plano (WinExe). Su objetivo principal es monitorear constantemente la generación de archivos de texto con lecturas de muestras, extraer la información y guardarla de forma estructurada en una base de datos SQL Server.

## 🚀 Características Principales

1. **Vigilancia en Tiempo Real:** Utiliza `FileSystemWatcher` para detectar inmediatamente cuando un archivo nuevo que empieza por `ONLINE` y termina en `.txt` es depositado en la carpeta de exportación.
2. **Barrido Inteligente (Anti-Apagones):** Al iniciarse, la aplicación realiza un barrido inicial de la carpeta para procesar cualquier archivo que se haya quedado rezagado mientras la computadora estuvo apagada o reiniciándose.
3. **Temporizador de Seguridad:** Cuenta con un cronómetro en segundo plano que realiza un barrido de seguridad cada 5 minutos. Esto garantiza que ningún archivo se quede atascado si ocurre algún error temporal del sistema operativo.
4. **Tolerancia a Escritura Lenta:** Si el sistema de origen sigue escribiendo en el archivo, la aplicación implementa una lógica de hasta 15 reintentos con esperas de 4 segundos (1 minuto de paciencia total) antes de marcar un error.
5. **Prevención de Duplicados:** La consulta de inserción a la base de datos está protegida con un bloque `WHERE NOT EXISTS`, asegurando que no se creen registros duplicados por nombre de archivo, código o fecha de muestreo.
6. **Mapeo Complejo con Regex:** Utiliza Expresiones Regulares para diseccionar y clasificar el código de muestra del archivo dependiendo de su estructura (Códigos del 1 al 9), dividiendo la cadena en variables como Lote, Cocina, Parada, Repetición, Destino, entre otros.
7. **Movimiento Automático:** Una vez que la información es extraída e insertada en SQL Server, el archivo `.txt` se traslada automáticamente a la carpeta de `Procesados`.

## ⚙️ Configuración y Despliegue

### Variables a configurar
Dentro de `Program.cs`, debes asegurar que las siguientes rutas apunten a los directorios correctos en tu servidor o máquina local:

```csharp
// Rutas de Producción
static string rutaExport = @"C:\Users\Public\Documents\Y15\Export";
static string rutaProcesados = @"C:\Users\Public\Documents\Y15\Procesados";

// Cadena de conexión a Base de Datos
static string connectionString = "Server=10.171.49.100;Database=ASISERVY_PROD;User Id=sa;Password=tu_password;TrustServerCertificate=True;";
```

### Ejecución Silenciosa
El archivo `.csproj` del proyecto está configurado con `<OutputType>WinExe</OutputType>`. Esto significa que, al compilarlo y ejecutarlo, **no mostrará una ventana de consola negra**. Correrá de forma completamente invisible en el sistema.

Para asegurar su funcionamiento 24/7, se recomienda configurarlo en el **Programador de Tareas de Windows** (Task Scheduler) para que se inicie automáticamente al encender el equipo, independientemente de si hay un usuario logueado o no.

## 🛠️ Tecnologías Utilizadas
- **Lenguaje:** C# (.NET 10.0)
- **Base de Datos:** SQL Server (`Microsoft.Data.SqlClient`)
- **Librerías Core:** `System.IO` (Manejo de archivos), `System.Threading` (Temporizadores y esperas), `System.Text.RegularExpressions` (Extracción de texto).
