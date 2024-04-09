using AzureSQL.Tables;
using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Sql;
using Microsoft.Extensions.Logging;

// using System.Collections.Generic;
// using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;


namespace My.Functions
{
    public class AzureDbFunctions
    {
        private readonly ILogger<AzureDbFunctions> _logger;

        public AzureDbFunctions(ILogger<AzureDbFunctions> logger)
        {
            _logger = logger;
        }

        // For demo, I've added a specific function to read all products
        // http://localhost:7071/products
        // Returns 404 if no products are found
        [Function("ReadAllProducts")]
        public IActionResult ReadAll([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "products")] 
            HttpRequest req,
            [SqlInput(commandText: "SELECT [id], [name], [price], [description] FROM dbo.Product",
                connectionStringSetting: "AZURE_SQL_CONNECTION_STRING")]
            IEnumerable<Product> products)
        {
            _logger.LogInformation("Read all products");
            return new OkObjectResult(products);
        }

        // Read a specific product by id
        // http://localhost:7071/product/id/5
        // Returns 404 if no product is found
        [Function("ReadProductById")]
        public IActionResult ReadProductById([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "product/{id}")] 
            HttpRequest req,
            [SqlInput(commandText: "SELECT * FROM dbo.Product WHERE [id] = @id",
                commandType: System.Data.CommandType.Text,
                parameters: "@id={id}",
                connectionStringSetting: "AZURE_SQL_CONNECTION_STRING")]
            IEnumerable<Product> products)
        {
            _logger.LogInformation("Read product");
            return new OkObjectResult(products);
        }

        // Example using a routing parameter for a category
        // Here to search all products by name
        // http://localhost:7071/product/name/productname
        // Returns 404 if no product is found
        [Function("ReadProductByName")]
        public IActionResult ReadProductByName([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "product/name/{name}")] 
            HttpRequest req,
            [SqlInput(commandText: @"SELECT * FROM dbo.Product WHERE name=@name",
                commandType: System.Data.CommandType.Text,
                parameters: "@name={name}",
                connectionStringSetting: "AZURE_SQL_CONNECTION_STRING")]
            IEnumerable<Product> products)
        {
            _logger.LogInformation(string.Format("Read product matching name"));
            return new OkObjectResult(products);

            // If I wanted return just one Product I would do this instead:
            // return new OkObjectResult(products.FirstOrDefault<Product>());
        }

        private string? GetConnectionString()
        {
            // Read the connection string either from the local.settings.json file 
            // or from the either App Settings and Connection Strings from Azure settings
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string? connectionString = config.GetConnectionString("AZURE_SQL_CONNECTION_STRING");
            if(string.IsNullOrEmpty(connectionString)) connectionString = config["AZURE_SQL_CONNECTION_STRING"];
            return connectionString;
        }

        // This function will return products that match the name and price included 
        // in the URL query string, or just the name, or just the price.
        // If no values are provided, it will return all products
        // http://localhost:7071/product
        // http://localhost:7071/product?name=productname
        // http://localhost:7071/product?price=100
        // http://localhost:7071/product?name=productname&price=100
        // Returns 404 if no products are found
        [Function("ReadProduct")]
        public IActionResult ReadProducts(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "product")] HttpRequest req,
            [FromQuery] string? name,
            [FromQuery] decimal? price
            )
        {
            string? connectionString = GetConnectionString();       
            string commandText = @"SELECT * FROM dbo.Product";

            if (!string.IsNullOrEmpty(name) && price.HasValue) commandText += string.Format(" WHERE [name] LIKE '%{0}%' AND [price] <= {1}", name, price);
            else if (!string.IsNullOrEmpty(name)) commandText += string.Format(" WHERE [name] LIKE '%{0}%'", name);
            else if (price.HasValue) commandText += string.Format(" WHERE [price] <= {0}", price);

            //_logger.LogInformation(string.Format("Name: {0} Price:{1} Connection string: {2} Command: {3}", name, price, connectionString, commandText));  
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                SqlCommand command = new SqlCommand(commandText, connection);
                List<Product> products = new List<Product>();

                if (!string.IsNullOrEmpty(name)) command.Parameters.AddWithValue("@name", name);
                if (price.HasValue) command.Parameters.AddWithValue("@price", price);

                try
                {
                    SqlDataReader reader;
                    connection.Open();
                    
                    reader = command.ExecuteReader();
                    if(!reader.HasRows) return new StatusCodeResult(StatusCodes.Status404NotFound);
                    while (reader.Read())
                    {
                        products.Add(new Product
                        {
                            id = (int)reader["id"],
                            name = (string)reader["name"],
                            price = (decimal)reader["price"],
                            description = (string)reader["description"]
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing SQL query");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                return new OkObjectResult(products);
            }
        }

        // This function will create a new product. The parameters are passed in the body of the request
        // The return from this function is used to create a new product in the database
        // http://localhost:7071/product
        [Function("CreateProduct")]
        [SqlOutput("dbo.Product", 
        connectionStringSetting: "AZURE_SQL_CONNECTION_STRING")]
        public Product CreateProduct([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "product")] 
            HttpRequest req,
            [FromBody] string name,
            [FromBody] decimal price,
            [FromBody] string description)
        {
            _logger.LogInformation(string.Format("Create product with name: {0}, price:{1}, description:{2}", name, price, description));
  
            // Since we do not provide an existing id, the product will be created
            return new Product
            {
                name = name,
                price = price,
                description = description
            };
        }


        // To perform a delete opertion you can create a stored procedure on the
        // database that will delete a product by id. Just use an SqlInput attribute to call
        // the stored procedure. A bit like this:
        // [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "DeleteFunction")] HttpRequest req
        // [SqlInput("dbo.DeleteProduct",
        // commandType = System.Data.CommandType.StoredProcedure, 
        // parameters = "@id={id}",
        // connectionStringSetting = "AZURE_SQL_CONNECTION_STRING")]

        // Delete a product by id
        // http://localhost:7071/product/4
        // Returns 404 if no products are found
        [Function("DeleteProduct")]
        public IActionResult DeleteProduct([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "product/{id}")] 
            HttpRequest req,
            [FromRoute] int id)
        {
            _logger.LogInformation(string.Format("Delete product with id: {0}", id));
            string? connectionString = GetConnectionString();       
            string commandText = @"DELETE FROM dbo.Product WHERE [id] = @id";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                SqlCommand command = new SqlCommand(commandText, connection);
                try
                {
                    connection.Open();

                    command.Parameters.AddWithValue("@id", id);
                    if(command.ExecuteNonQuery() > 0) return new StatusCodeResult(StatusCodes.Status200OK);
                    else return new StatusCodeResult(StatusCodes.Status404NotFound);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing SQL query");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
        }

        // This function performs a pragmatic patch to edit the product.
        // The return from this function is upserted into the database
        // "upsert": if the id is found, the product is updated, if not, it is created
        // SqlOutput used T-SQL MERGE
        // http://localhost:7071/product/upsert/4
        [Function("UpsertProduct")]
        [SqlOutput("dbo.Product", 
        connectionStringSetting: "AZURE_SQL_CONNECTION_STRING")]
        public Product UpsertProduct([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "product/upsert/{id}")] 
            HttpRequest req,
            [FromRoute] int id,      
            [FromBody] string name,
            [FromBody] decimal price,
            [FromBody] string description)
        {
            _logger.LogInformation(string.Format("Edit product with id: {0}, name: {1}, price:{2}, description:{3}", id, name, price, description));
  
            return new Product
            {
                id = id,
                name = name,
                price = price,
                description = description
            };
        }

        // This function performs a patch to edit the product.
        // http://localhost:7071/product/7
        // Returns 404 if no products are found
        [Function("PatchProduct")]
        public IActionResult PatchProduct([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "product/{id}")] 
            HttpRequest req,
            [FromRoute] int id,
            [FromBody] string name,
            [FromBody] decimal price,
            [FromBody] string description)
        {
            _logger.LogInformation(string.Format("Pragmatic patch the product with id: {0}", id));
            string? connectionString = GetConnectionString();       
            string commandText = @"UPDATE Product
                                SET 
                                    name = CASE WHEN @name IS NOT NULL THEN @name ELSE name END,
                                    price = CASE WHEN @price IS NOT NULL THEN @price ELSE price END,
                                    description = CASE WHEN @description IS NOT NULL THEN @description ELSE description END
                                WHERE
                                    id = @id;";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                SqlCommand command = new SqlCommand(commandText, connection);
                try
                {
                    connection.Open();
                    command.Parameters.AddWithValue("@id", id);
                    command.Parameters.AddWithValue("@name", name);
                    command.Parameters.AddWithValue("@price", price);
                    command.Parameters.AddWithValue("@description", description);
                    if(command.ExecuteNonQuery() > 0) return new StatusCodeResult(StatusCodes.Status200OK);
                    else return new StatusCodeResult(StatusCodes.Status404NotFound);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing SQL query");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
        }

    }
}
