.PHONY: all run clean

all:
	@printf '%s\n' \
	'<Project Sdk="Microsoft.NET.Sdk">' \
	'  <PropertyGroup>' \
	'    <OutputType>Exe</OutputType>' \
	'    <TargetFramework>net8.0</TargetFramework>' \
	'    <ImplicitUsings>disable</ImplicitUsings>' \
	'    <Nullable>enable</Nullable>' \
	'    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>' \
	'  </PropertyGroup>' \
	'</Project>' > UltraSudoku.csproj
	dotnet build -c Release

run: all
	dotnet run -c Release --no-build

clean:
	rm -rf bin obj UltraSudoku.csproj
