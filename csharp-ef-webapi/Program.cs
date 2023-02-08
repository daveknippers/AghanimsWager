using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var vueFrontEndOrigins = "vueFrontEnd";

if (builder.Environment.IsDevelopment())
{
    //Set CORS
    builder.Services.AddCors(
        options =>
        {
            options.AddPolicy(name: vueFrontEndOrigins,
                                policy =>
                                {
                                    policy.WithOrigins("http://localhost:8080",
                                                        "http://localhost:9000")
                                        .AllowAnyHeader()
                                        .WithMethods("GET","POST")
                                        .AllowCredentials();
                                });
        }
    );
}


// Add Database
builder.Services.AddDbContextFactory<AghanimsWagerContext>();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(vueFrontEndOrigins);

app.UseAuthorization();

app.MapControllers();

app.Run();
