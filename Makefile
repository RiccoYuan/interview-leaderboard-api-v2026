CONFIG ?= Release

.PHONY: all restore build test benchmark ci clean

all: restore build test benchmark

restore:
	dotnet restore

build: restore
	dotnet build -c $(CONFIG)

test: build
	dotnet test -c $(CONFIG) --no-build

benchmark: build
	dotnet run -c $(CONFIG) --no-build --project tests/Leaderboard.Benchmarks -- --job Dry --filter *IndexComparison*

ci: test benchmark

clean:
	dotnet clean
	rm -rf BenchmarkDotNet.Artifacts
