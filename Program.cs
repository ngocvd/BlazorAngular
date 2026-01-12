var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();
builder.Services.AddSession(options =>
{
  options.IdleTimeout = TimeSpan.FromMinutes(300);
  options.Cookie.Name = "ngocvd.session";
  options.Cookie.IsEssential = true;
  options.Cookie.HttpOnly = true;
  options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
  options.Cookie.SameSite = SameSiteMode.Strict;
  //options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
