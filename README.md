# olspy
A *very* basic interface for the internal overleaf API.

## What it can do
- Automatically detect running overleaf containers
- Retrieve all documents (i.e. text files) from a project
- Retrieve some metadata about a project (name, description, etc.)
- Retrieve the compiled PDF for a project
### What it *can't* do
- Enumerate existing projects
- List or search document filenames
- List or open binary files
- Compile with custom main files
- Interact with any real-time/frontend APIs
- Open a sharing link

## Setting up
To use this program, you need to allow external access to overleaf's internal services by adding
```
	LISTEN_ADDRESS=0.0.0.0
	WEB_API_USER=sharelatex
	WEB_API_PASSWORD=<your password here>
```
to your overleaf instance's `config/variables.env`.

The values for WEB_API_PASSWORD and WEB_API_USER need to be passed to `Overleaf.SetCredentials()` to use internal APIs (i.e. document compilation).

### Please Note
Exposing this API to the internet might cause security problems.

`WEB_API_PASSWORD` can be used to bypass authentication checks and should be a hard-to-guess password.

If you don't use a reverse proxy, using something more restrictive than 0.0.0.0 would be advised.
