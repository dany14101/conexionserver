using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using MySql.Data.MySqlClient;
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

        //diccionario de emojis
        private Dictionary<string, Image> emojis = new Dictionary<string, Image>();
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
            //Detenemos acciones
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
                //Leemos las posibles opciones del cliente
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
                                            res += "usuario_lista|" + idUsuario + "|" + nombreUsuario + "|" + emailUsuario + ";";

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
                    if (partes[0] == "agregar_miembros")
                    {
                        string res = "";
                        using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                        {
                            try
                            {
                                conexion.Open();
                                //Hacemos una lista con los ids de los usuarios a agregar
                                List<int> idusuariosf = new List<int>();
                                string query = "INSERT INTO miembros_grupos (id_usuario, id_grupo) VALUES (@idu, @idg)";
                                using (MySqlCommand comando = new MySqlCommand(query, conexion))
                                {
                                    int idGrupo = int.Parse(partes[2]);
                                   
                                    comando.Parameters.Add("@idu", MySqlDbType.Int32);
                                    comando.Parameters.Add("@idg", MySqlDbType.Int32).Value = idGrupo;

                                    string[] idsusuarios = partes[3].Split(',');
                                    int miembrosAgregados = 0;

                                    foreach (string idUsuarioStr in idsusuarios)
                                    {
                                        if (int.TryParse(idUsuarioStr, out int idUsuario))
                                        {
                                            comando.CommandText = @"INSERT IGNORE INTO miembros_grupos (id_usuario, id_grupo) VALUES (@idu, @idg);";
                                            comando.Parameters["@idu"].Value = idUsuario;
                                            int result = comando.ExecuteNonQuery();
                                            if (result > 0)
                                            {
                                                miembrosAgregados++;
                                                idusuariosf.Add(idUsuario);
                                            }
                                        }
                                    }
                                    string nombregr="";
                                    //Checar el nombre del grupo
                                    using (MySqlCommand cmdEmail = new MySqlCommand("SELECT Nombre_grupo FROM grupos WHERE clave_grupo=@id", conexion))
                                    {
                                        cmdEmail.Parameters.AddWithValue("@id", partes[2]);
                                        object result1 = cmdEmail.ExecuteScalar();
                                        if (result1 != null)
                                        {
                                            nombregr = result1.ToString();
                                        }
                                    }
                                    //Notificamos a los usuarios agregados si estan conectados
                                    foreach (int idUsuario in idusuariosf)
                                    {
                                        //Obtenemos el email del usuario
                                        string emailUsuario = "";
                                        using (MySqlCommand cmdEmail = new MySqlCommand("SELECT email FROM usuarios WHERE id=@id", conexion))
                                        {
                                            cmdEmail.Parameters.AddWithValue("@id", idUsuario);
                                            object result = cmdEmail.ExecuteScalar();
                                            if (result != null)
                                            {
                                                emailUsuario = result.ToString();
                                            }
                                        }
                                        lock (lockUsuarios)
                                        {
                                            if (usuariosConectados.ContainsKey(emailUsuario))
                                            {
                                                TcpClient cli = usuariosConectados[emailUsuario];
                                                string mensaje = "agregar_grupos|" + nombregr;
                                                Tcpcod.Enviar(cli,mensaje);
                                            }
                                        }
                                    }
                                    //Usamos la notacion para los usuarios agregados
                                    res = "Usuarios agregados:"+miembrosAgregados;
                                    byte[] datos = Encoding.UTF8.GetBytes(res);
                                    flujo.Write(datos, 0, datos.Length);
                                }
                            }
                            catch (Exception ex)
                            {
                                res = "Error al agregar miembros: " + ex.Message;
                                byte[] datos = Encoding.UTF8.GetBytes(res);
                                flujo.Write(datos, 0, datos.Length);
                            }
                        }
                    }
                    if (partes[0] == "buscar_grupo")
                    {
                        checargrupos(partes, cliente);
                    }
                    if (partes[0] == "cargar_mensajes")
                    {
                        string nombreGrupo = partes[1];
                        cargarMensajesGrupo(nombreGrupo, cliente);
                    }
                    if (partes[0] == "guardar_mensaje")
                    {
                        int idUsuario = int.Parse(partes[1]);
                        int idGrupo = int.Parse(partes[2]);
                        string contenido = partes[3];
                        guardarMensaje(idUsuario, idGrupo, contenido, cliente);
                    }
                    //Nuevo mensaje lo envia a todos los demas miembros del grupo
                    if (partes[0] == "nuevo_mensaje")
                    {
                        int idUsuario = int.Parse(partes[1]);
                        int idGrupo = int.Parse(partes[2]);
                        string contenido = partes[3];
                        mandarm(idUsuario, idGrupo, contenido,cliente);
                    }
                    if (partes[0] == "Obtenerclave")
                    {
                        //id==clave del grupo
                        string nombreGrupo = partes[1];

                        using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                        {
                                conexion.Open();
                                string query = "SELECT clave_grupo FROM grupos WHERE Nombre_grupo = @nombreGrupo";

                                using (MySqlCommand comando = new MySqlCommand(query, conexion))
                                {
                                    comando.Parameters.AddWithValue("@nombreGrupo", nombreGrupo);
                                    //Se usa object para poder usar cualquier tipo de dato
                                    object resultado = comando.ExecuteScalar();

                                    string respuesta;

                                    if (resultado != null)
                                    {
                                        string idGrupo = resultado.ToString();
                                        respuesta = "OK|"+idGrupo;
                                    }
                                    else
                                    {
                                        //manda error
                                        respuesta = "error|";
                                    }

                                    byte[] datos = Encoding.UTF8.GetBytes(respuesta);
                                    flujo.Write(datos, 0, datos.Length);
                                }
                        }
                    }
                    if (partes[0] == "Mostrargrupo")
                    {
                        using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                        {
                            try
                            {
                                conexion.Open();

                                string res = "";

                                //Obtener las claves del grupo segun el id del usuario
                                string queryClave = "SELECT id_grupo FROM miembros_grupos WHERE id_usuario = @id";
                                string claveGrupo = "";

                                using (MySqlCommand comando = new MySqlCommand(queryClave, conexion))
                                {
                                    comando.Parameters.AddWithValue("@id", partes[1]);
                                    using (MySqlDataReader leer = comando.ExecuteReader())
                                    {
                                        if (leer.Read())
                                        {
                                            claveGrupo = leer["id_grupo"].ToString();
                                        }
                                    }
                                }

                                if (string.IsNullOrEmpty(claveGrupo))
                                {
                                    res = "error|";
                                }
                                else
                                {
                                    //Buscar los grupos en los que esta el cliente
                                    string queryGrupos = @"SELECT g.id, g.Nombre_grupo FROM grupos g JOIN miembros_grupos mg ON g.clave_grupo = mg.id_grupo WHERE mg.id_usuario = @idUsuario";

                                    using (MySqlCommand comando2 = new MySqlCommand(queryGrupos, conexion))
                                    {
                                        comando2.Parameters.AddWithValue("@idUsuario", partes[1]); 

                                        using (MySqlDataReader leer2 = comando2.ExecuteReader())
                                        {
                                            while (leer2.Read())
                                            {
                                                string nombreGrupo = leer2["Nombre_grupo"].ToString();
                                                res += nombreGrupo+";";
                                            }
                                        }
                                    }

                                    if (string.IsNullOrEmpty(res))
                                    {
                                        res = "error|";
                                    }
                                    //Juntar clave y res
                                    res = res+ ";";
                                }
                                byte[] datos = Encoding.UTF8.GetBytes(res);
                                flujo.Write(datos, 0, datos.Length);
                                flujo.Flush();
                            }
                            catch (Exception ex)
                            {
                                string res = "1|" + ex.Message;
                                byte[] datos = Encoding.UTF8.GetBytes(res);
                                flujo.Write(datos, 0, datos.Length);
                                flujo.Flush();
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

                //Se usa lock para que no choquen los clientes
                lock (clientes)
                {

                }
            }
        }



        //Funcion de agregar usuario
        private void agregausuario(string usuario, string contrasena, TcpClient cliente)
        {
            // Variables de alamcenamiento de datos
            string email = usuario;
            string password = contrasena;

            string hashedcontra = "";
            string idUsuario = "";
            string nombreUsuario = "";

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
            //consulta de la contraseña en la base de datos
            try
            {
                using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
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
                    //Obtenemos el flujo del cliente que esta iniciando
                    NetworkStream flujo1= cliente.GetStream();
                    usuariosConectados.Add(email,cliente);
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
                //Obtiene datos del usuario
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
            //Contraseña deshasheada
            string hashedPass = PasswordHelper.HashPassword(partes[3]);
            string respuesta;
            MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING);
            NetworkStream flujo;
            //guarda en base de datos
            try
            {
                if (conexion.State != ConnectionState.Open)
                {
                    conexion.Open();
                }
                //checa si email existe
                if (EmailExiste(partes[2], conexion))
                {
                    //Enviar error al cliente
                    flujo = cliente.GetStream();
                    respuesta = "6|El correo ya está registrado.";
                    byte[] datos = Encoding.UTF8.GetBytes(respuesta);
                    flujo.Write(datos, 0, datos.Length);
                    return;
                }
                //Insertar nuevo usuario
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
                    using (comando = new MySqlCommand("INSERT INTO grupos (clave_grupo,Nombre_grupo) values(@clav,@nom) ;", conexion))
                    {
                        comando.Parameters.AddWithValue("@clav", id);
                        comando.Parameters.AddWithValue("@nom", nombreGrupo);
                        comando.ExecuteNonQuery();
                    }


                    // Obtener id del grupo  usamos lastinsertid
                    comando = new MySqlCommand("SELECT LAST_INSERT_ID() as id", conexion);
                    leer = comando.ExecuteReader();
                    int idGrupoRecienCreado = -1;
                    if (leer.Read())
                    {
                        idGrupoRecienCreado = leer.GetInt32("id"); 
                    }
                    comando.Dispose();
                    leer.Close();

                    if (idGrupoRecienCreado == -1)
                    {
                        flujo = cliente.GetStream();
                        string respuesta1 = "";
                        byte[] datos2 = Encoding.UTF8.GetBytes(respuesta1);
                        flujo.Write(datos2, 0, datos2.Length);
                        return;
                    }
                    string clavegrupo="";
                    //Obtenemos la clave del grupo recien creado
                    using (comando = new MySqlCommand("SELECT clave_grupo FROM grupos WHERE id = @idGrupo", conexion))
                    {
                        comando.Parameters.AddWithValue("@idGrupo", idGrupoRecienCreado);
                        using (leer = comando.ExecuteReader())
                        {
                            if (leer.Read())
                            {
                                clavegrupo = leer["clave_grupo"].ToString();
                            }
                        }
                    }
                    using (comando = new MySqlCommand("INSERT into miembros_grupos(id_usuario,id_grupo) values(@idu,@idg) ;", conexion))
                    {
                        comando.Parameters.AddWithValue("@idu", idusuariocrea); 
                        comando.Parameters.AddWithValue("@idg", clavegrupo);
                        comando.ExecuteNonQuery();
                    }

                    //Regresar mensaje de exito
                    flujo = cliente.GetStream();
                    string respuesta = "7|" + id + "|" + idusuariocrea;
                    byte[] datos1 = Encoding.UTF8.GetBytes(respuesta);
                    flujo.Write(datos1, 0, datos1.Length);
                    return;

                }
            }
            catch (Exception ex)
            {
                flujo = cliente.GetStream();
                string respuesta1 = "8|error al crear grupo" + ex.Message;
                byte[] datos2 = Encoding.UTF8.GetBytes(respuesta1);
                flujo.Write(datos2, 0, datos2.Length);
            }
        }



        private void checargrupos(string[] partes, TcpClient cliente)
        {
            string simi = partes[1];  
            string idUsuario = partes[2]; 
            string res = "0|";
            NetworkStream flujo = cliente.GetStream();

            using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
            {
                try
                {
                    conexion.Open();

                    string query = @"SELECT DISTINCT g.clave_grupo, g.Nombre_grupo FROM grupos g LEFT JOIN miembros_grupos mg ON g.clave_grupo = mg.id_grupo  WHERE (g.Nombre_grupo LIKE @simi OR g.clave_grupo LIKE @simi) AND (mg.id_usuario = @idUsuario OR mg.id_usuario IS NULL);";

                    using (MySqlCommand comando = new MySqlCommand(query, conexion))
                    {
                        comando.Parameters.AddWithValue("@simi", "%" + simi + "%");
                        comando.Parameters.AddWithValue("@idUsuario", idUsuario);

                        using (MySqlDataReader leer = comando.ExecuteReader())
                        {
                            while (leer.Read())
                            {
                                string claveGrupo = leer["clave_grupo"].ToString();
                                string nombreGrupo = leer["Nombre_grupo"].ToString();

                                //enviar datos
                                res += nombreGrupo+"|";
                            }
                        }
                    }

                    if (res == "0|")
                    {
                        res = "0|No se encontraron grupos coincidentes";
                    }

                    byte[] datos = Encoding.UTF8.GetBytes(res);
                    flujo.Write(datos, 0, datos.Length);
                }
                catch (Exception ex)
                {
                    string error = "1|" + ex.Message;
                    byte[] datos = Encoding.UTF8.GetBytes(error);
                    flujo.Write(datos, 0, datos.Length);
                }
            }
        }



        //Codigo para guardar mensaje a la base de datos
        private void guardarMensaje(int idUsuario, int clave, string contenido, TcpClient cliente)
        {
            try
            {
                int idg;
                //Obtenemos el id del grupo
                using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                {
                    conexion.Open();
                    string sql1= "SELECT id FROM grupos WHERE clave_grupo = @clave";
                    using (MySqlCommand cmdGetId = new MySqlCommand(sql1, conexion))
                    {
                        cmdGetId.Parameters.AddWithValue("@clave", clave);
                        //Object para cualquier tipo de dato
                        object result = cmdGetId.ExecuteScalar();
                        if (result == null)
                        {
                            string error = "5|error|Grupo no encontrado";
                            _ = enviaraus(cliente, error);
                            return;
                        }
                        idg = Convert.ToInt32(result);
                    }
                }
                using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                {
                    conexion.Open();
                    string sql = "INSERT INTO mensajes (Id_usuario, Id_grupo, contenido) VALUES (@idu, @idg, @cont)";
                    using (MySqlCommand cmd = new MySqlCommand(sql, conexion))
                    {
                        cmd.Parameters.AddWithValue("@idu", idUsuario);
                        cmd.Parameters.AddWithValue("@idg", idg);
                        cmd.Parameters.AddWithValue("@cont", contenido);
                        cmd.ExecuteNonQuery();
                    }
                }
                //Manda el mensaje a los demas miembros
                mandarm(idUsuario, idg, contenido,cliente);
                string respuesta = "5|ok";
                _ = enviaraus(cliente, respuesta);
            }
            catch (Exception ex)
            {
                string error = "5|error|" + ex.Message;
                _ = enviaraus(cliente, error);
            }
        }

        //Manda mensaje a todos los miembros del grupo
        private void mandarm(int idUsuario, int idGrupo, string contenido,TcpClient cliente)
        {
            string nombreUsuario = "";
            string claveGrupo = idGrupo.ToString();
            using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
            {
                conexion.Open();
                //Obtener la clave de grupo 

                //obtener el nombre del usuario de todas las personas en el grupo
                using (MySqlCommand cmdGetName = new MySqlCommand("SELECT id_usuario FROM miembros_grupos WHERE id_grupo=@id", conexion))
                {
                    //Lee el id del grupo
                    cmdGetName.Parameters.AddWithValue("@id", claveGrupo);
                    using (MySqlDataReader reader = cmdGetName.ExecuteReader())
                    {
                        List<int> miembrosIds = new List<int>();
                        //Checamos si el id esta conectado
                        while (reader.Read())
                        {
                            miembrosIds.Add(Convert.ToInt32(reader["id_usuario"]));
                        }
                        reader.Close();
                        //Obtener el nombre del usuario que envio el mensaje
                        using (MySqlCommand cmdGetSenderName = new MySqlCommand("SELECT nombre FROM usuarios WHERE id=@idUsuario", conexion))
                        {
                            cmdGetSenderName.Parameters.AddWithValue("@idUsuario", idUsuario);
                            object result = cmdGetSenderName.ExecuteScalar();
                            if (result != null)
                            {
                                nombreUsuario = result.ToString();
                            }
                        }
                        //Enviar el mensaje a todos los miembros del grupo excepto al remitente
                        foreach (int miembroId in miembrosIds)
                        {
                            if (miembroId != idUsuario)
                            {
                                //Obtener el email del miembro
                                string emailMiembro = "";
                                using (MySqlCommand cmdGetEmail = new MySqlCommand("SELECT email FROM usuarios WHERE id=@id", conexion))
                                {
                                    cmdGetEmail.Parameters.AddWithValue("@id", miembroId);
                                    object result = cmdGetEmail.ExecuteScalar();
                                    if (result != null)
                                    {
                                        emailMiembro = result.ToString();
                                    }
                                }
                                //Enviar el mensaje si el miembro está conectado
                                lock (lockUsuarios)
                                {
                                    if(usuariosConectados.ContainsKey(emailMiembro))
                                    {
                                        //Obtenemos el TCP del cliente activo
                                        TcpClient cli = usuariosConectados[emailMiembro];
                                        string mensaje = "mensaje_nuevo|" + claveGrupo + "|" + nombreUsuario + "|" + contenido;
                                        _ = enviaraus(cli, mensaje);
                                    }
                                }
                            }
                        }
                    }
                }


            }
        }
        //cargar mensajes de grupo
        private void cargarMensajesGrupo(string nombreGrupo, TcpClient cliente)
        {
            try
            {
                using (MySqlConnection conexion = new MySqlConnection(MYSQL_CONNECTION_STRING))
                {
                    conexion.Open();

                    //obtener el id del grupo
                    using (MySqlCommand cmdGetId = new MySqlCommand("SELECT id FROM grupos WHERE Nombre_grupo=@nom", conexion))
                    {
                        cmdGetId.Parameters.AddWithValue("@nom", nombreGrupo);
                        //Object para cualquier tipo de dato
                        object result = cmdGetId.ExecuteScalar();

                        if (result == null)
                        {
                            string error = "4|ERROR|Grupo no encontrado";
                            _ = enviaraus(cliente, error);
                            return;
                        }

                        int idGrupo = Convert.ToInt32(result);

                        //obtiene los mensajes del grupo
                        string sql = @"SELECT m.contenido, m.fecha, m.Id_usuario, u.nombre AS nombre_usuario FROM mensajes m JOIN usuarios u ON m.Id_usuario = u.id WHERE m.Id_grupo=@id  ORDER BY m.fecha ASC";

                        using (MySqlCommand cmdMensajes = new MySqlCommand(sql, conexion))
                        {
                            cmdMensajes.Parameters.AddWithValue("@id", idGrupo);
                            using (MySqlDataReader reader = cmdMensajes.ExecuteReader())
                            {
                                string resultado = "";
                                while (reader.Read())
                                {
                                    resultado += reader["nombre_usuario"] + "|" + reader["contenido"] + "|" + reader["fecha"] + ";";
                                }
                                _ = enviaraus(cliente, resultado);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string error = "4|error|" + ex.Message;
                _=enviaraus(cliente, error);
            }
        }

        private async Task enviaraus(TcpClient cliente, string mensaje)
        {
            try
            {
                if (!mensaje.EndsWith("\n"))
                {
                    mensaje = mensaje + "\n";
                }
                NetworkStream net=cliente.GetStream();
                byte[] bytes = Encoding.UTF8.GetBytes(mensaje);
                net.Write(bytes, 0, bytes.Length);
                net.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al enviar mensaje al cliente: " + ex.Message);
            }
        }
    }
}


public static class Tcpcod
{
    //Envía un mensaje a un cliente TcpClient
    public static void Enviar(TcpClient cliente, string mensaje)
    {
        try
        {
            if (cliente?.Connected == true)
            {
                byte[] datos = Encoding.UTF8.GetBytes(mensaje);
                cliente.GetStream().Write(datos, 0, datos.Length);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error al enviar mensaje al cliente: " + ex.Message);
        }
    }
    //Envía un mensaje a un flujo de red NetworkStream
    public static void Enviar1(NetworkStream flujo, string mensaje)
    {
        try
        {
            if (flujo != null && flujo.CanWrite)
            {
                byte[] datos = Encoding.UTF8.GetBytes(mensaje);
                flujo.Write(datos, 0, datos.Length);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error al enviar mensaje: " + ex.Message);
        }
    }
}
//Desifra contraseñas y verifica
public static class PasswordHelper
{
    public static string HashPassword(string password)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            StringBuilder builder = new StringBuilder();
            foreach (byte b in bytes)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }

    public static bool VerifyPassword(string enteredPassword, string storedHash)
    {
        string enteredHash = HashPassword(enteredPassword);
        return string.Equals(enteredHash, storedHash, StringComparison.OrdinalIgnoreCase);
    }
}

