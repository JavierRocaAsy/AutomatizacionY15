using System;
using System.IO;
using System.Threading;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace AutomatizacionY15
{
    class Program
    {
        // Pruebas
        //static string rutaExport = @"C:\Pruebas_Y15\Export";
        //static string rutaProcesados = @"C:\Pruebas_Y15\Procesados";
        //static string connectionString = "Server=10.171.49.100;Database=ASIS_PRE_PROD;User Id=sa;Password=P4ssw0rd;TrustServerCertificate=True;";

        // Produccion
        static string rutaExport = @"C:\Users\Public\Documents\Y15\Export";
        static string rutaProcesados = @"C:\Users\Public\Documents\Y15\Procesados";
        static string connectionString = "Server=10.171.49.100;Database=ASISERVY_PROD;User Id=sa;Password=P4ssw0rd;TrustServerCertificate=True;";

        private static System.Timers.Timer _timerSeguridad;

        static void Main(string[] args)
        {
            Directory.CreateDirectory(rutaExport);
            Directory.CreateDirectory(rutaProcesados);

            // 1. Barrido inicial al arrancar
            Console.WriteLine("Realizando barrido inicial de archivos rezagados...");
            EscanearCarpetaRezagados();

            FileSystemWatcher watcher = new FileSystemWatcher
            {
                Path = rutaExport,
                Filter = "ONLINE*.txt",
                EnableRaisingEvents = true
            };

            watcher.Created += OnCreated;

            // 2. Red de seguridad: Timer que escanea cada 5 minutos
            _timerSeguridad = new System.Timers.Timer(300000); // 300,000 ms = 5 minutos
            _timerSeguridad.Elapsed += (sender, e) => EscanearCarpetaRezagados();
            _timerSeguridad.AutoReset = true;
            _timerSeguridad.Enabled = true;

            Console.WriteLine("=========================================");
            Console.WriteLine($"Vigilando carpeta: {rutaExport}");
            Console.WriteLine("Barrido de seguridad configurado cada 5 min.");
            Console.WriteLine("=========================================\n");

            // Evitar que la aplicación se cierre
            Thread.Sleep(Timeout.Infinite);
        }

        private static void EscanearCarpetaRezagados()
        {
            try
            {
                string[] archivos = Directory.GetFiles(rutaExport, "ONLINE*.txt");
                foreach (string filePath in archivos)
                {
                    string fileName = Path.GetFileName(filePath);
                    Console.WriteLine($"[Barrido de Seguridad] Procesando archivo rezagado: {fileName}");
                    ProcesarArchivo(filePath, fileName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error en Barrido] {ex.Message}");
            }
        }

        private static void OnCreated(object sender, FileSystemEventArgs e)
        {

            if (!e.Name.StartsWith("ONLINE", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Console.WriteLine($"[Detectado] Nuevo archivo: {e.Name}");
            ProcesarArchivo(e.FullPath, e.Name);
        }

        private static bool ExisteArchivoEnBD(string nombreArchivo)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT COUNT(1) FROM MUESTREOS_Y15 WHERE NombreArchivo = @Archivo";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Archivo", nombreArchivo);
                    int count = Convert.ToInt32(cmd.ExecuteScalar());
                    return count > 0;
                }
            }
        }

        private static void ProcesarArchivo(string filePath, string fileName)
        {

            // Damos un margen inicial por si se acaba de crear
            Thread.Sleep(2000);

            // Aumentamos a 15 reintentos de 4 segundos (1 minuto de paciencia total)
            int reintentos = 15;
            while (reintentos > 0)
            {
                try
                {
                    // Si el archivo ya no existe, significa que el Timer u otro evento ya lo procesó y movió
                    if (!File.Exists(filePath)) return;
                  
                    string[] lineas = File.ReadAllLines(filePath);
                    Console.WriteLine($"[Extracción] Archivo {fileName} leído. Contiene {lineas.Length} líneas. Procesando...");

                    InsertarLineasEnBD(fileName, lineas);

                    string destino = Path.Combine(rutaProcesados, fileName);
                    if (File.Exists(destino)) File.Delete(destino);

                    File.Move(filePath, destino);
                    Console.WriteLine($"[Completado] Archivo {fileName} movido a Procesados.\n");
                    break;
                }
                catch (IOException)
                {
                    Console.WriteLine($"[Advertencia] Archivo {fileName} en uso, reintentando... ({reintentos} intentos restantes)");
                    reintentos--;
                    Thread.Sleep(4000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error Grave] Problema procesando {fileName}: {ex.Message}");
                    break;
                }
            }
        }
        
        private static void InsertarLineasEnBD(string nombreArchivo, string[] lineas)
        {
            int filasInsertadas = 0;
            int filasIgnoradas = 0;

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();

                string query = @"INSERT INTO MUESTREOS_Y15
                       (NombreArchivo, CodigoMuestra, Codigo, Repeticion, Lote, Cocina, Parada, Destino, LMT, OrdenFabricacion, FechaProduccion, Descripcion, TiempoExposicion, Tanque, Barco, Marea, Especie, Parametro, Resultado, Unidad, FechaMuestreo, Turno, Muestras, Identificacion) 
                       SELECT @Archivo, @CodigoMuestra, @Codigo, @Repeticion, @Lote, @Cocina, @Parada, @Destino, @LMT, @OF, @FechaP, @Desc, @TiempoE, @Tanque, @Barco, @Marea, @Especie, @Parametro, @Resultado, @Unidad, @FechaM, @Turno, @Muestras, @Identificacion
                       WHERE NOT EXISTS (
                           SELECT 1 FROM MUESTREOS_Y15 
                           WHERE NombreArchivo = @Archivo 
                             AND CodigoMuestra = @CodigoMuestra 
                             AND FechaMuestreo = @FechaM
                       )";

                foreach (string linea in lineas)
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;

                    string[] columnas = linea.Split('\t');

                    if (columnas.Length >= 6)
                    {
                        string parametro = columnas[1].Trim();

                        if (!parametro.Contains("(mg/Kg)")) continue;
                        parametro = parametro.Replace("(mg/Kg)", "").Trim();

                        string codigoMuestraRaw = columnas[0].Trim();

                        string valCodigo = "";
                        string valRepeticion = "";
                        string valLote = "";
                        string valCocina = "";
                        string valParada = "";
                        string valDestino = "";
                        string valLMT = "";
                        string valOF = "";
                        string valFechaProduccion = "";
                        string valDescripcion = "";
                        string valTiempoExposicion = "";
                        string valTanque = "";
                        string valBarco = "";
                        string valMarea = ""; 
                        string valEspecie = "";
                        string valTurno = "A"; // Por defecto Turno A
                        string valMuestras = "";
                        string valIdentificacion = "";

                        // Lógica para Código 1
                        if (Regex.IsMatch(codigoMuestraRaw, @"^1(?!\d)"))
                        {
                            valCodigo = "1";
                            if (codigoMuestraRaw.TrimEnd().EndsWith(" B", StringComparison.OrdinalIgnoreCase)) valTurno = "B";
                            Match m1 = Regex.Match(codigoMuestraRaw, @"^1\s*(R[P]?\d*)?\s*([a-zA-Z]\d?)[\s-]*(\d+)\s*-\s*(\d+)", RegexOptions.IgnoreCase);
                            
                            if (m1.Success)
                            {
                                valRepeticion = m1.Groups[1].Value.ToUpper(); 
                                valLote = m1.Groups[2].Value.ToUpper();      
                                valCocina = m1.Groups[3].Value;             
                                valParada = m1.Groups[4].Value;               
                            }
                        }
                        // Lógica para Código 2
                        else if (Regex.IsMatch(codigoMuestraRaw, @"^2(?!\d)"))
                        {
                            valCodigo = "2";
                            if (codigoMuestraRaw.TrimEnd().EndsWith(" B", StringComparison.OrdinalIgnoreCase)) valTurno = "B";
                            Match m2 = Regex.Match(codigoMuestraRaw, @"^2\s*(R[P]?\d*)?\s*([a-zA-Z]\d?)\s+(EM|PR)\s+([a-zA-Z]+)\s*(R[P]?\d*)?", RegexOptions.IgnoreCase);

                            if (m2.Success)
                            {
                                valRepeticion = string.IsNullOrEmpty(m2.Groups[1].Value) ? m2.Groups[5].Value.ToUpper() : m2.Groups[1].Value.ToUpper();
                                valLote = m2.Groups[2].Value.ToUpper();
                                valDestino = m2.Groups[3].Value.ToUpper();
                                valLMT = m2.Groups[4].Value.ToUpper();
                            }
                        }
                        // Lógica para Código 3 y 4
                        else if (Regex.IsMatch(codigoMuestraRaw, @"^[34](?!\d)"))
                        {
                            valCodigo = codigoMuestraRaw.Substring(0, 1);
                            Match m34 = Regex.Match(codigoMuestraRaw, @"^[34]\s*(R[P]?\d+(?=\s)|R[P]?)?\s*(\d+)", RegexOptions.IgnoreCase);

                            if (m34.Success)
                            {
                                valRepeticion = m34.Groups[1].Value.ToUpper();
                                valOF = m34.Groups[2].Value;
                            }
                        }
                        // Lógica para Código 5
                        else if (Regex.IsMatch(codigoMuestraRaw, @"^5(?!\d)"))
                        {
                            valCodigo = "5";
                            Match m5 = Regex.Match(codigoMuestraRaw, @"^5\s+(\d+)\s+(\d{6})\s+(.*)", RegexOptions.IgnoreCase);

                            if (m5.Success)
                            {
                                valMuestras = m5.Groups[1].Value;
                                
                                // Dar formato a la fecha: de "101226" a "10/12/2026"
                                string rawFecha = m5.Groups[2].Value;
                                valFechaProduccion = $"{rawFecha.Substring(0, 2)}/{rawFecha.Substring(2, 2)}/20{rawFecha.Substring(4, 2)}"; 
                                
                                string resto = m5.Groups[3].Value.Trim().ToUpper();

                                if (resto.Length == 2 && (resto.StartsWith("L") || resto.StartsWith("M")))
                                {
                                    valDescripcion = resto.Substring(0, 1); 
                                    valDestino = resto.Substring(1, 1);     
                                }
                                else
                                {
                                    if (resto.Length >= 2)
                                    {
                                        valLote = resto.Substring(0, resto.Length - 1); 
                                        valTiempoExposicion = resto.Substring(resto.Length - 1); 
                                    }
                                }
                            }
                        }
                        // Lógica para Código 6
                        else if (Regex.IsMatch(codigoMuestraRaw, @"^6(?!\d)"))
                        {
                            valCodigo = "6";
                            string[] partes6 = codigoMuestraRaw.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                            if (partes6.Length >= 3)
                            {
                                bool tieneRepeticion = partes6[1].StartsWith("R", StringComparison.OrdinalIgnoreCase) && partes6[1].Length <= 3;

                                int idxTanque = tieneRepeticion ? 2 : 1;
                                int idxDatosCompactos = tieneRepeticion ? 3 : 2;

                                valRepeticion = tieneRepeticion ? partes6[1].ToUpper() : "";
                                valTanque = partes6[idxTanque];

                                string datosCompactos = partes6[idxDatosCompactos];

                                if (datosCompactos.Length >= 4)
                                {
                                    valBarco = datosCompactos.Substring(0, 2);
                                    valEspecie = datosCompactos.Substring(datosCompactos.Length - 1, 1);
                                    valMarea = datosCompactos.Substring(2, datosCompactos.Length - 3);
                                }
                            }
                        }
                        // Lógica para Código 7
                        else if (Regex.IsMatch(codigoMuestraRaw, @"^7(?!\d)"))
                        {
                            valCodigo = "7";
                            Match m7 = Regex.Match(codigoMuestraRaw, @"^7\s+(R[P]?\d*)?\s*(\d{6})\s+(.*)", RegexOptions.IgnoreCase);
                            if (m7.Success)
                            {
                                valRepeticion = m7.Groups[1].Value.ToUpper();
                                string rawFecha = m7.Groups[2].Value;
                                valFechaProduccion = $"{rawFecha.Substring(0, 2)}/{rawFecha.Substring(2, 2)}/20{rawFecha.Substring(4, 2)}"; 
                                valLote = m7.Groups[3].Value.ToUpper();
                            }
                        }
                        // Lógica para Código 8
                        else if (Regex.IsMatch(codigoMuestraRaw, @"^8(?!\d)"))
                        {
                            valCodigo = "8";
                            Match m8 = Regex.Match(codigoMuestraRaw, @"^8\s+(R[P]?\d*)?\s*(\S+)\s+(\d{6})", RegexOptions.IgnoreCase);
                            if (m8.Success)
                            {
                                valRepeticion = m8.Groups[1].Value.ToUpper();
                                valIdentificacion = m8.Groups[2].Value.ToUpper();
                                string rawFecha = m8.Groups[3].Value;
                                valFechaProduccion = $"{rawFecha.Substring(0, 2)}/{rawFecha.Substring(2, 2)}/20{rawFecha.Substring(4, 2)}"; 
                            }
                        }
                        // Lógica para Código 9
                        else if (Regex.IsMatch(codigoMuestraRaw, @"^9(?!\d)"))
                        {
                            valCodigo = "9";
                            Match m9 = Regex.Match(codigoMuestraRaw, @"^9\s+(R[P]?\d*)?\s*(.*)", RegexOptions.IgnoreCase);
                            if (m9.Success)
                            {
                                valRepeticion = m9.Groups[1].Value.ToUpper();
                                valIdentificacion = m9.Groups[2].Value.ToUpper();
                            }
                        }
                        // Lógica temporal para los demás códigos
                        else
                        {
                            string[] partes = codigoMuestraRaw.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (partes.Length > 0 && char.IsDigit(partes[0][0]))
                            {
                                valCodigo = partes[0];
                                
                                // Buscar si la segunda parte es una Repetición (Ej: R1, RP2, R, RP)
                                if (partes.Length > 1 && Regex.IsMatch(partes[1], @"^R[P]?\d*$", RegexOptions.IgnoreCase))
                                {
                                    valRepeticion = partes[1].ToUpper();
                                }
                            }
                        }

                        using (SqlCommand cmd = new SqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@Archivo", nombreArchivo);
                            cmd.Parameters.AddWithValue("@CodigoMuestra", codigoMuestraRaw);
                            cmd.Parameters.AddWithValue("@Codigo", valCodigo);
                            cmd.Parameters.AddWithValue("@Repeticion", valRepeticion);
                            cmd.Parameters.AddWithValue("@Lote", valLote);
                            cmd.Parameters.AddWithValue("@Cocina", valCocina);
                            cmd.Parameters.AddWithValue("@Parada", valParada);
                            cmd.Parameters.AddWithValue("@Destino", valDestino);
                            cmd.Parameters.AddWithValue("@LMT", valLMT);
                            cmd.Parameters.AddWithValue("@OF", valOF);
                            cmd.Parameters.AddWithValue("@FechaP", valFechaProduccion);
                            cmd.Parameters.AddWithValue("@Desc", valDescripcion);
                            cmd.Parameters.AddWithValue("@TiempoE", valTiempoExposicion);
                            cmd.Parameters.AddWithValue("@Tanque", valTanque);
                            cmd.Parameters.AddWithValue("@Barco", valBarco);
                            cmd.Parameters.AddWithValue("@Marea", valMarea);
                            cmd.Parameters.AddWithValue("@Especie", valEspecie);
                            cmd.Parameters.AddWithValue("@Parametro", parametro);
                            cmd.Parameters.AddWithValue("@Resultado", columnas[3].Trim());
                            cmd.Parameters.AddWithValue("@Unidad", columnas[4].Trim());
                            cmd.Parameters.AddWithValue("@FechaM", columnas[5].Trim());
                            cmd.Parameters.AddWithValue("@Turno", valTurno);
                            cmd.Parameters.AddWithValue("@Muestras", valMuestras);
                            cmd.Parameters.AddWithValue("@Identificacion", valIdentificacion);

                            int filasAfectadas = cmd.ExecuteNonQuery();

                            if (filasAfectadas > 0)
                            {
                                filasInsertadas++; 
                            }
                            else
                            {
                                filasIgnoradas++; 
                            }
                        }
                    }
                }
            }
            Console.WriteLine($"[Éxito] Se guardaron {filasInsertadas} registros perfectamente divididos.");
        }

    }
}