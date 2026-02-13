# Contributing to BayrolConnect

Thank you for your interest in contributing to BayrolConnect! We welcome contributions from the community.

## How to Contribute

### Reporting Issues

- Check if the issue already exists in the [issue tracker](https://github.com/DarkStarDS9/BayrolConnect/issues)
- Provide detailed information about the issue, including:
  - Steps to reproduce
  - Expected behavior
  - Actual behavior
  - Environment details (OS, .NET version, device type)
  - Log output if applicable

### Submitting Pull Requests

1. **Fork the repository** and create a new branch from `main`
2. **Make your changes**:
   - Follow the existing code style
   - Add tests for new functionality
   - Update documentation as needed
3. **Test your changes**:
   - Run `dotnet build` to ensure the code compiles
   - Run `dotnet test` to ensure all tests pass
   - Test with your actual Bayrol device if possible
4. **Commit your changes**:
   - Use clear, descriptive commit messages
   - Reference any related issues
5. **Submit a pull request**:
   - Provide a clear description of the changes
   - Explain why the change is needed
   - Link to any related issues

## Code Style

- Follow standard C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Use structured logging (avoid string interpolation in log messages)
- Keep methods focused and single-purpose

## Testing

- Add unit tests for new functionality
- Ensure all existing tests pass
- Test edge cases and error conditions
- Integration testing with real devices is appreciated but not required

## Security

- Never commit credentials or sensitive data
- Follow the security best practices in [SECURITY.md](SECURITY.md)
- Report security vulnerabilities privately (see SECURITY.md)

## Questions?

Feel free to open an issue for questions or discussions about potential contributions.

## License

By contributing to BayrolConnect, you agree that your contributions will be licensed under the same license as the project (see [LICENSE](LICENSE)).
