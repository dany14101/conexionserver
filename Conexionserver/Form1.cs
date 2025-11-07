using Google.Protobuf.WellKnownTypes;
using Microsoft.Win32;
using MySql.Data.MySqlClient;
using MySqlX.XDevAPI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Org.BouncyCastle.Asn1.Cmp.Challenge;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;
namespace Conexionserver
{
    public partial class Form1 : Form
    {
        private MySqlCommand comando;
        private MySqlDataReader leer;
        private Thread hilo;
        TcpListener servidor;
        //Lista de clientes
        private List<TcpClient> clientes = new List<TcpClient>();
        private Dictionary<string, TcpClient> usuariosConectados = new Dictionary<string, TcpClient>();
        private readonly object lockUsuarios = new object();
        private const string MYSQL_CONNECTION_STRING = "Server = localhost; Port=3306;Database=chat;Uid=root;Pwd=Alex";
        public Form1()
        {
            InitializeComponent();

        }
        //Inicia servidor
        private void iniser()
        {
            try
            {
                servidor = new TcpListener(IPAddress.Any, 8080);
                servidor.Start();

                //Checa el puerto
                this.Invoke((MethodInvoker)(() =>
                {
                    label2.Text = "Servidor iniciado en el puerto 8080";
                }));

                while (true)
                {
                    TcpClient cliente = servidor.AcceptTcpClient();
                    //Agrega cliente a lista
                    Thread hiloCliente = new Thread(() => Clientes(cliente));
                    hiloCliente.IsBackground = true;
                    hiloCliente.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al iniciar servidor: " + ex.Message);
            }
        }
        //Iniciamos el hilo
        private void Form1_Load(object sender, EventArgs e)
        {
            hilo = new Thread(iniser);
            hilo.IsBackground = true;
            hilo.Start();
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            servidor.Stop();
            Environment.Exit(0);
        }
        //Basado en referencia lo ajuste para que vea cada funcion de cada modulo
        private void Clientes(TcpClient cliente)
        {
            NetworkStream flujo = cliente.GetStream();
            lock (clientes)
            {
                if (!clientes.Contains(cliente))
                {
                    clientes.Add(cliente);
                }
            }

            byte[] buffer = new byte[2048];
            int bytesLeidos;

            try
            {
                while ((bytesLeidos = flujo.Read(buffer, 0, buffer.Length)) != 0)
                {
                    string mensajer = Encoding.UTF8.GetString(buffer, 0, bytesLeidos);
                    string[] partes = mensajer.Split('|');
                    //Checa el apartado de inicio de sesion
                    if (partes[0] == "1")
                    {
                        agregausuario(partes[1], partes[2], cliente);
                    }
                    //Checa el registro de mensjes
                    if (partes[0] == "2")
                    {
                        Registro(partes, cliente);
                    }
                    if (partes[0] == "3")
                    {
                        cregrupo(partes, cliente);
                    }
                    if (partes[0] == "lista_miembros")
                    {
                        using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                        {
                            try
                            {
                                string res = "";
                                conexion.Open();
                                // Query: Selecciona todos los usuarios cuyo ID NO sea el ID del creador
                                string query = "SELECT id, nombre, email FROM usuarios WHERE id!=@idCreador AND id NOT IN (SELECT id_usuario FROM miembros_grupos WHERE id_grupo=@idGrupo)";
                                using (MySqlCommand comando = new MySqlCommand(query, conexion))
                                {
                                    comando.Parameters.AddWithValue("@idCreador", partes[2]);
                                    comando.Parameters.AddWithValue("@idGrupo", partes[1]);

                                    using (MySqlDataReader leer = comando.ExecuteReader())
                                    {
                                        while (leer.Read())
                                        {
                                            //Crea una cadena con los usuarios nombre y email
                                            string idUsuario = leer["id"].ToString();
                                            string nombreUsuario = leer["nombre"].ToString();
                                            string emailUsuario = leer["email"].ToString();
                                            res+="usuario_lista|" + idUsuario + "|" + nombreUsuario + "|" + emailUsuario+";";

                                        }
                                        //Envía la lista de usuarios al cliente
                                        byte[] datos = Encoding.UTF8.GetBytes(res);
                                        flujo.Write(datos, 0, datos.Length);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                string res = "1" + ex.Message;
                                byte[] datos = Encoding.UTF8.GetBytes(res);
                                flujo.Write(datos, 0, datos.Length);
                            }
                        }
                    }
                    if (partes[0]=="agregar_miembros")
                    {
                        // 2. Insertar múltiples miembros en la base de datos
                        using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                        {
                            try
                            {
                                conexion.Open();
                                string query = "INSERT INTO miembros_grupos (id_usuario, id_grupo) VALUES (@idu, @idg)";

                                using (MySqlCommand comando = new MySqlCommand(query, conexion))
                                {
                                    // Preparar los parámetros fijos (solo el ID del grupo)
                                    comando.Parameters.Add("@idg", MySqlDbType.Int32).Value = partes[2];
                                    comando.Parameters.Add("@idu", MySqlDbType.Int32); // Este se actualizará en el loop

                                    int miembrosAgregados = 0;
                                    string [] idsusuarios = partes[3].Split(',');
                                    foreach (string idUsuarioStr in idsusuarios)
                                    {
                                        if (int.TryParse(idUsuarioStr, out int idUsuario))
                                        {
                                            comando.Parameters["@idu"].Value = idUsuario;
                                            comando.ExecuteNonQuery();
                                            miembrosAgregados++;
                                        }
                                    }

                                    // Solo mostramos un mensaje de éxito si realmente se agregaron miembros
                                    if (miembrosAgregados > 0)
                                    {
                                        //Mensaje de éxito pasar como cadena a cliente
                                        string res = "0|" + miembrosAgregados;
                                        byte[] datos = Encoding.UTF8.GetBytes(res);
                                        flujo.Write(datos, 0, datos.Length);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // Si falla el insert, igual intentamos regresar al chat
                                string res = "1"+ex.Message;
                                byte[] datos = Encoding.UTF8.GetBytes(res);
                                flujo.Write(datos, 0, datos.Length);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error cliente: " + ex.Message);
            }
            finally
            {
                flujo.Close();
                cliente.Close();
                lock (clientes)
                {
                    clientes.Remove(cliente);
                }
                lock (lockUsuarios)
                {
                    string quitar = null;
                    foreach (var pair in usuariosConectados)
                    {
                        if (pair.Value == cliente)
                        {
                            quitar = pair.Key;
                            break;
                        }
                    }

                    if (quitar != null)
                    {
                        usuariosConectados.Remove(quitar);
                        Console.WriteLine("Usuario desconectado y eliminado de la lista.");
                    }
                }
            }
        }

        

        //Funcion de agregar usuario
        private void agregausuario(string usuario, string contrasena, TcpClient cliente)
        {
            // Variables de alamcenamiento de datos
            string email = usuario;
            string password = contrasena;

            string hashedcontra = string.Empty;
            string idUsuario = string.Empty;
            string nombreUsuario = string.Empty;

            NetworkStream flujo = cliente.GetStream();
            lock (lockUsuarios)
            {
                if (usuariosConectados.ContainsKey(email))
                {
                    //Quito de la lista de usuarios conectados
                    clientes.Remove(cliente);
                    usuariosConectados.Remove(email);
                    string resp = "1|Usuario iniciado";
                    byte[] datos = Encoding.UTF8.GetBytes(resp);
                    flujo.Write(datos, 0, datos.Length);
                    return;
                }
            }
            //Bloque de Código para la consulta de la contraseña en la base de datos
            try
            {
                using ( MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                {
                    conexion.Open();
                    //Consulta de la contraseña para el email proporcionado
                    string query = "SELECT id, password, nombre FROM usuarios WHERE email = @email";

                    comando = new MySqlCommand(query, conexion);
                    comando.Parameters.AddWithValue("@email", email);

                    leer = comando.ExecuteReader();

                    if (leer.Read())
                    {
                        //Si encuentra el usuario, obtiene los datos necesarios
                        hashedcontra = leer["password"].ToString();
                        //Capturamos el id y nombre del usuario
                        idUsuario = leer["id"].ToString();
                        nombreUsuario = leer["nombre"].ToString();
                    }
                    leer.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al conectar con la base de datos: " + ex.Message);
                return;
            }


            if (string.IsNullOrEmpty(hashedcontra))
            {
                // Si no se encuentra el usuario
                //Quito de la lista de usuarios conectados
                clientes.Remove(cliente);
                usuariosConectados.Remove(email);
                string resp = "2|Usuario no encontrado";
                byte[] datos = Encoding.UTF8.GetBytes(resp);
                flujo.Write(datos, 0, datos.Length);
                return;
            }
            bool contravalida = PasswordHelper.VerifyPassword(password, hashedcontra);
            string respuesta = string.Empty;
            lock (lockUsuarios)
            {
                if (!usuariosConectados.ContainsKey(email))
                {
                    usuariosConectados[email] = cliente;
                }
            }
            if (contravalida)
            {
                //Regres de respuest un -1 de que se logro iniciar sesion
                using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                {
                    conexion.Open();
                    //obtiene el email,id del usuario y nombre
                    string consulta = "SELECT id, email, nombre FROM usuarios WHERE email = @correo";
                    using (comando = new MySqlCommand(consulta, conexion))
                    {
                        comando.Parameters.AddWithValue("@correo", email);
                        using (leer = comando.ExecuteReader())
                        {
                            if (leer.Read())
                            {
                                idUsuario = leer["id"].ToString();
                                email = leer["email"].ToString();
                                nombreUsuario = leer["nombre"].ToString();
                                respuesta = "0|" + idUsuario + "|" + email + "|" + nombreUsuario;
                            }
                        }
                    }
                }
                byte[] datos = Encoding.UTF8.GetBytes(respuesta);
                flujo.Write(datos, 0, datos.Length);
            }
            else
            {
                //Contraseña incorrecta
                //Regres de respuest un 0 de que no se logro iniciar sesion
                usuariosConectados.Remove(email);
                //Quito de la lista de usuarios conectados
                clientes.Remove(cliente);
                respuesta = "3|Contraseña incorrecta";
                byte[] datos = Encoding.UTF8.GetBytes(respuesta);
                flujo.Write(datos, 0, datos.Length);
            }
        }


        //Registro de mensajes
        private void Registro(string[] partes, TcpClient cliente)
        {
            // HASH DE LA CONTRASEÑA
            string hashedPass = PasswordHelper.HashPassword(partes[3]);
            string respuesta;
            MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING);
            NetworkStream flujo;
            // GUARDAR EN BASE DE DATOS
            try
            {
                if (conexion.State != ConnectionState.Open)
                {
                    conexion.Open();
                }
                // Verificar si el email ya existe
                if (EmailExiste(partes[2], conexion))
                {
                    //Enviar error al cliente
                    flujo = cliente.GetStream();
                    respuesta = "6|El correo ya está registrado.";
                    byte[] datos = Encoding.UTF8.GetBytes(respuesta);
                    flujo.Write(datos, 0, datos.Length);
                    return;
                }
                // Insertar nuevo usuario
                string query = "INSERT INTO usuarios (nombre, email, password,fecha) VALUES (@nombre, @correo, @password,@fecha)";

                using (comando = new MySqlCommand(query, conexion))
                {
                    comando.Parameters.AddWithValue("@nombre", partes[1]);
                    comando.Parameters.AddWithValue("@correo", partes[2]);
                    comando.Parameters.AddWithValue("@password", hashedPass);
                    comando.Parameters.AddWithValue("@fecha", partes[4]);
                    comando.ExecuteNonQuery();
                }
                flujo = cliente.GetStream();
                respuesta = "4|Usuario agregado";
                byte[] datos1 = Encoding.UTF8.GetBytes(respuesta);
                flujo.Write(datos1, 0, datos1.Length);
                return;


            }
            catch (Exception ex)
            {
                flujo = cliente.GetStream();
                respuesta = "5|No se pudo agregar" + ex.Message;
                byte[] datos1 = Encoding.UTF8.GetBytes(respuesta);
                flujo.Write(datos1, 0, datos1.Length);
            }
            finally
            {
                if (conexion.State == ConnectionState.Open && conexion != null)
                {
                    conexion.Close();
                }
            }
        }

        private bool EmailExiste(string email, MySqlConnection conexion)
        {
            string query = "SELECT COUNT(id) FROM usuarios WHERE email = @email";
            using (MySqlCommand comando = new MySqlCommand(query, conexion))
            {
                comando.Parameters.AddWithValue("@email", email);
                int count = Convert.ToInt32(comando.ExecuteScalar());
                return count > 0;
            }
        }


        //Crear grupo
        private void cregrupo(string[] partes, TcpClient cliente)
        {
            string nombreGrupo = partes[2];
            string idusuariocrea = partes[3];
            string id = partes[1];
            NetworkStream flujo;
            //guarda en base de datos
            try
            {
                using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                {
                    conexion.Open();
                    // Insertar grupo
                    using (comando = new MySqlCommand("INSERT INTO grupos (clave_grupo,Nombre_grupo) \r\nvalues(@clav,@nom) ;", conexion))
                    {
                        comando.Parameters.AddWithValue("@clav", id);
                        comando.Parameters.AddWithValue("@nom", nombreGrupo);
                        comando.ExecuteNonQuery();
                    }


                    // Obtener id del grupo (OPTIMIZADO: Usamos LAST_INSERT_ID() para mayor fiabilidad)
                    comando = new MySqlCommand("SELECT LAST_INSERT_ID() as id", conexion);
                    leer = comando.ExecuteReader();
                    int idGrupoRecienCreado = -1;
                    if (leer.Read())
                    {
                        idGrupoRecienCreado = leer.GetInt32("id"); // Usamos GetInt32 para el ID
                    }
                    comando.Dispose();
                    leer.Close();

                    if (idGrupoRecienCreado == -1)
                    {
                        flujo = cliente.GetStream();
                        string respuesta1 = "8|ERROR AL CREAR GRUPO";
                        byte[] datos2 = Encoding.UTF8.GetBytes(respuesta1);
                        flujo.Write(datos2, 0, datos2.Length);
                        return;
                    }

                    //Insertamos al usuario CREADOR en miembrros grupos
                    using (comando = new MySqlCommand("INSERT into miembros_grupos(id_usuario,id_grupo) \r\nvalues(@idu,@idg) ;", conexion))
                    {
                        comando.Parameters.AddWithValue("@idu", idusuariocrea); // USAMOS EL ID DEL USUARIO CREADOR
                        comando.Parameters.AddWithValue("@idg", idGrupoRecienCreado);
                        comando.ExecuteNonQuery();
                    }

                    //Regresar mensaje de exito
                    flujo = cliente.GetStream();
                    string respuesta = "7|" + idusuariocrea + "|" + id;
                    byte[] datos1 = Encoding.UTF8.GetBytes(respuesta);
                    flujo.Write(datos1, 0, datos1.Length);
                    return;

                }
            }
            catch (Exception ex)
            {
                flujo = cliente.GetStream();
                string respuesta1 = "8|ERROR AL CREAR GRUPO" + ex.Message;
                byte[] datos2 = Encoding.UTF8.GetBytes(respuesta1);
                flujo.Write(datos2, 0, datos2.Length);
            }
        }


        ///Funcions de chat principal
        ///
        private void cargarEmojis()
        {
            emojis[":smile:"] = Image.FromFile(Path.Combine(Application.StartupPath, @"..\..\Resources\smile.png"));
            emojis[":heart:"] = Image.FromFile(Path.Combine(Application.StartupPath, @"..\..\Resources\heart.png"));
            emojis[":sad:"] = Image.FromFile(Path.Combine(Application.StartupPath, @"..\..\Resources\sad.png"));
        }

    }
}


public static class PasswordHelper
{
    //Metodo para hashear la contraseña usando SHA256
    public static string HashPassword(string password)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            StringBuilder builder = new StringBuilder();
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }
    }

    public static bool VerifyPassword(string enteredPassword, string storedHash)
    {
        string enteredHash = HashPassword(enteredPassword);
        return string.Equals(enteredHash, storedHash, StringComparison.OrdinalIgnoreCase);
    }
}