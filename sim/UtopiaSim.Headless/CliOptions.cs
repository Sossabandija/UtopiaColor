using System.Globalization;

namespace UtopiaSim.Headless;

sealed class CliOptions
{
    public string ConfigPath { get; private set; } = Path.Combine("configs", "original.json");
    public double Days { get; private set; } = 30;
    public ulong? Seed { get; private set; }
    public int Runs { get; private set; } = 1;
    public string? OutDir { get; private set; }
    public int SampleEvery { get; private set; } = 150;
    public bool WriteGenes { get; private set; }
    public bool StopWhenExtinct { get; private set; } = true;
    public int Parallel { get; private set; } = Environment.ProcessorCount;
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Value()
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"Falta el valor de {a}.");
                return args[++i];
            }
            switch (a)
            {
                case "-c" or "--config": o.ConfigPath = Value(); break;
                case "-d" or "--days": o.Days = ParseDouble(a, Value(), min: 0.0001); break;
                case "-s" or "--seed": o.Seed = ParseULong(a, Value()); break;
                case "-r" or "--runs": o.Runs = (int)ParseDouble(a, Value(), min: 1); break;
                case "-o" or "--out": o.OutDir = Value(); break;
                case "--sample-every": o.SampleEvery = (int)ParseDouble(a, Value(), min: 1); break;
                case "--genes": o.WriteGenes = true; break;
                case "--no-stop": o.StopWhenExtinct = false; break;
                case "-p" or "--parallel": o.Parallel = (int)ParseDouble(a, Value(), min: 1); break;
                case "-h" or "--help": o.ShowHelp = true; break;
                default: throw new ArgumentException($"Opción desconocida: {a}");
            }
        }
        return o;
    }

    static double ParseDouble(string name, string s, double min)
    {
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < min)
            throw new ArgumentException($"{name}: '{s}' no es un número válido (mínimo {min}).");
        return v;
    }

    static ulong ParseULong(string name, string s) =>
        ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong v)
            ? v
            : throw new ArgumentException($"{name}: '{s}' no es un entero válido.");

    public static void PrintHelp() => Console.WriteLine("""
        utopiasim — corre la simulación sin gráficos y guarda los resultados en CSV.

        Uso (desde la carpeta sim/):
          dotnet run -c Release --project UtopiaSim.Headless -- [opciones]

        Opciones:
          -c, --config <ruta>     Configuración JSON (por defecto configs/original.json)
          -d, --days <n>          Días a simular (por defecto 30; acepta decimales)
          -s, --seed <n>          Semilla de la primera corrida (por defecto la del JSON)
          -r, --runs <n>          Cuántas corridas, con semillas consecutivas (por defecto 1)
          -o, --out <carpeta>     Dónde guardar (por defecto runs/<config>-<fecha>)
              --sample-every <t>  Cada cuántos ticks anotar poblaciones (por defecto 150; 4500 ticks = 1 día)
              --genes             Guardar también genes.csv: los genes de cada ser, una vez al día
              --no-stop           No detenerse cuando se extinguen todos los animales
          -p, --parallel <n>      Corridas en paralelo (por defecto, núcleos del PC)
          -h, --help              Esta ayuda

        Ejemplos:
          dotnet run -c Release --project UtopiaSim.Headless -- -d 30
          dotnet run -c Release --project UtopiaSim.Headless -- -c configs/etapa.json -d 100 -r 20 --genes
        """);
}
