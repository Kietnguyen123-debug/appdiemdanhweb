using appdiemdanh.DTOs;

namespace appdiemdanh.Services
{
    public interface IGeminiChatService
    {
        Task<ChatResponseDto> ChatAsync(ChatRequestDto request, UserJwtContext userContext);
    }
}